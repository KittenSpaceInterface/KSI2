using Brutal;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Extensions;
using Brutal.Memory;
using Brutal.Numerics;
using Brutal.Pointers;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;
using System.Runtime.InteropServices;

namespace KSI2;

internal readonly record struct PanelMaskNineSlice(
    float SourceCornerFraction, float DestinationCornerFractionX, float DestinationCornerFractionY);

// Drawn only in KSA's ImGui pass, after KSA has transitioned its scene image for sampling.
internal sealed unsafe class PanelBackgroundBlur : RenderTechnique
{
    private struct PanelData
    {
        public float4 Rect;
        public float4 Scene;
        public float4 MaskSlice;
        public float4 Fill;
        public float4 Method;
        public float4 MaskUvRect;
    }

    private struct PanelDrawData
    {
        public PanelData Panel;
        public VkDescriptorSet Descriptor;
        public VkDescriptorSet FallbackDescriptor;
    }

    private sealed class MaskDescriptor
    {
        public required VkImageView View;
        public required VkDescriptorSet Set;
        public VkDescriptorSet FallbackSet;
    }

    private const int MasksPerDescriptorPool = 5;

    private static readonly ImDrawCallback DrawCallback = OnDraw;
    private static PanelBackgroundBlur? _active;
    private readonly DescriptorSetLayoutEx _layout;
    private readonly Dictionary<TextureReference, MaskDescriptor> _masks = new();
    private readonly List<nint> _pendingCallbackData = [];
    private readonly List<DescriptorPoolEx> _additionalDescriptorPools = [];
    private DescriptorPoolEx _activeDescriptorPool;
    private int _activePoolMaskCount;
    private VkImageView _sceneView;
    private VkImageView _fallbackSceneView;
    private VkImageView _compositionSceneView;
    private readonly PanelBlurProcess? _process;
    private readonly PtrOwner<VkPipelineColorBlendAttachmentState>? _processedBlendAttachment;
    private readonly PtrOwner<VkPipelineColorBlendStateCreateInfo>? _processedBlendState;
    private VkRenderPass _otherPass;
    private VkPipeline _otherPipeline;
    private bool _disposed;
    private bool _failed;
    internal bool Available => !_failed && !_disposed;

    private PanelBackgroundBlur(RendererContext context, PanelBlurProcess? process)
        : base("KSI2 panel background blur", context, context.RenderPassInfo,
            [ModLibrary.Get<ShaderReference>("KSI2PanelBlurVert"),
             ModLibrary.Get<ShaderReference>("KSI2PanelBlurFrag")])
    {
        _process = process;
        try
        {
            var device = context.Renderer.Device;
            KsaGraphicsLifetime.EnsureInstalled(device);
            if (process is not null)
            {
                // RGB is scene-corrected and premultiplied in the shader. Alpha uses KSP2's captured factors.
                _processedBlendAttachment = new(new VkPipelineColorBlendAttachmentState
                {
                    BlendEnable = true,
                    SrcColorBlendFactor = VkBlendFactor.One,
                    DstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                    ColorBlendOp = VkBlendOp.Add,
                    SrcAlphaBlendFactor = VkBlendFactor.SrcAlpha,
                    DstAlphaBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                    AlphaBlendOp = VkBlendOp.Add,
                    ColorWriteMask = VkColorComponentFlags.RBit | VkColorComponentFlags.GBit
                        | VkColorComponentFlags.BBit | VkColorComponentFlags.ABit
                });
                _processedBlendState = new(new VkPipelineColorBlendStateCreateInfo
                {
                    AttachmentCount = 1,
                    Attachments = _processedBlendAttachment
                });
            }
            VkDescriptorPoolSize poolSize = new()
            {
                Type = VkDescriptorType.CombinedImageSampler,
                DescriptorCount = MasksPerDescriptorPool * 6
            };
            DescriptorPool = device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo
            {
                MaxSets = MasksPerDescriptorPool * 2,
                PoolSizes = new Span<VkDescriptorPoolSize>(ref poolSize)
            }, null);
            _activeDescriptorPool = DescriptorPool;
            Span<VkDescriptorSetLayoutBinding> bindings = stackalloc VkDescriptorSetLayoutBinding[3];
            for (var i = 0; i < bindings.Length; i++)
                bindings[i] = new VkDescriptorSetLayoutBinding
                {
                    Binding = i,
                    DescriptorType = VkDescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    StageFlags = VkShaderStageFlags.FragmentBit
                };
            _layout = device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo
            {
                Bindings = bindings
            }, null);
            VkDescriptorSetLayout setLayout = _layout;
            VkPushConstantRange pushRange = new()
            {
                StageFlags = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit,
                Offset = ByteSize.Zero,
                Size = ByteSize.Of<PanelData>()
            };
            PipelineLayout = device.CreatePipelineLayout(new Span<VkDescriptorSetLayout>(ref setLayout),
                new Span<VkPushConstantRange>(ref pushRange), null);
            RebuildFrameResources();
            KsaGraphicsLifetime.Register(Release);
        }
        catch (Exception ex)
        {
            KsaGraphicsLifetime.ObserveFailure(ex);
            if (KsaGraphicsLifetime.CanUse(Renderer.Device))
            {
                base.Dispose();
                if (_layout is not null) Renderer.Device.DestroyDescriptorSetLayout(_layout, null);
            }
            _processedBlendState?.Dispose();
            _processedBlendAttachment?.Dispose();
            throw;
        }
    }

    internal static PanelBackgroundBlur? Create(PanelBlurProcess? process = null)
    {
        try
        {
            if (ModLibrary.Get<ShaderReference>("KSI2PanelBlurVert").Shader is null ||
                ModLibrary.Get<ShaderReference>("KSI2PanelBlurFrag").Shader is null)
                throw new InvalidOperationException("Blur shaders were not loaded.");
            return new PanelBackgroundBlur(KSA.Program.GetGaugeContext(), process);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"KSI2: panel blur unavailable: {ex}");
            return null;
        }
    }

    internal bool Queue(ImDrawListPtr draw, float2 min, float2 max, TextureReference mask,
        float opacity, bool flipVertically = false, PanelMaskNineSlice? maskNineSlice = null,
        bool rectangularMask = false, uint solidColor = 0xFD190B03u, float4? maskUvRect = null)
    {
        try
        {
            if (_failed || _disposed || !KsaGraphicsLifetime.CanUse(Renderer.Device)) return false;
            var fallback = KSA.Program.OffscreenTarget?.ColorImage;
            var scene = _process is null ? fallback : _process.Output;
            var compositionScene = _process is null ? fallback : _process.SceneColor;
            if (scene is null || fallback is null || compositionScene is null) return false;
            if (scene.Width <= 0 || scene.Height <= 0 || mask.ImageView.IsNull()) return false;
            if (_sceneView.VkHandle != scene.ImageView.VkHandle
                || _fallbackSceneView.VkHandle != fallback.ImageView.VkHandle
                || _compositionSceneView.VkHandle != compositionScene.ImageView.VkHandle)
            {
                // Descriptor changes must not race the prior frame's command buffer.
                if (!KsaGraphicsLifetime.TryWaitIdle(Renderer.Device)) return false;
                _sceneView = scene.ImageView;
                _fallbackSceneView = fallback.ImageView;
                _compositionSceneView = compositionScene.ImageView;
                foreach (var entry in _masks.Values)
                {
                    UpdateImages(_sceneView, entry.View, _compositionSceneView, entry.Set);
                    if (_process is not null) UpdateImages(_fallbackSceneView, entry.View, _fallbackSceneView, entry.FallbackSet);
                }
            }
            if (!_masks.TryGetValue(mask, out var maskDescriptor))
            {
                if (_activePoolMaskCount >= MasksPerDescriptorPool)
                {
                    _activeDescriptorPool = CreateMaskDescriptorPool();
                    _additionalDescriptorPools.Add(_activeDescriptorPool);
                    _activePoolMaskCount = 0;
                }
                maskDescriptor = new MaskDescriptor
                {
                    View = mask.ImageView,
                    Set = Renderer.Device.AllocateDescriptorSet(_activeDescriptorPool, _layout),
                    FallbackSet = _process is null ? default : Renderer.Device.AllocateDescriptorSet(_activeDescriptorPool, _layout)
                };
                UpdateImages(_sceneView, maskDescriptor.View, _compositionSceneView, maskDescriptor.Set);
                if (_process is not null) UpdateImages(_fallbackSceneView, maskDescriptor.View, _fallbackSceneView, maskDescriptor.FallbackSet);
                _masks.Add(mask, maskDescriptor);
                _activePoolMaskCount++;
            }
            else if (maskDescriptor.View.VkHandle != mask.ImageView.VkHandle)
            {
                if (!KsaGraphicsLifetime.TryWaitIdle(Renderer.Device)) return false;
                maskDescriptor.View = mask.ImageView;
                UpdateImages(_sceneView, maskDescriptor.View, _compositionSceneView, maskDescriptor.Set);
                if (_process is not null) UpdateImages(_fallbackSceneView, maskDescriptor.View, _fallbackSceneView, maskDescriptor.FallbackSet);
            }
            var viewport = ImGui.GetMainViewport();
            var data = new PanelDrawData
            {
                Panel = new PanelData
                {
                    Rect = new float4((min.X - viewport.Pos.X) / viewport.Size.X,
                        (min.Y - viewport.Pos.Y) / viewport.Size.Y,
                        (max.X - min.X) / viewport.Size.X, (max.Y - min.Y) / viewport.Size.Y),
                    Scene = new float4(0f, 0f, Math.Clamp(opacity, 0f, 1f), flipVertically ? 1f : 0f),
                    MaskSlice = rectangularMask
                        ? new float4(0f, 0f, 0f, 2f)
                        : maskNineSlice is { } slice
                        ? new float4(slice.SourceCornerFraction, slice.DestinationCornerFractionX,
                            slice.DestinationCornerFractionY, 1f)
                        : float4.Zero,
                    Fill = new float4((solidColor & 255) / 255f, ((solidColor >> 8) & 255) / 255f,
                        ((solidColor >> 16) & 255) / 255f, (solidColor >> 24) / 255f),
                    Method = new float4(_process is null ? 0 : 1, _process is null ? 0 : 1, 0, 0),
                    MaskUvRect = maskUvRect ?? new float4(0, 0, 1, 1)
                },
                Descriptor = maskDescriptor.Set,
                FallbackDescriptor = maskDescriptor.FallbackSet
            };
            var callbackData = (PanelDrawData*)NativeMemory.Alloc((nuint)sizeof(PanelDrawData));
            if (callbackData is null) return false;
            *callbackData = data;
            _pendingCallbackData.Add((nint)callbackData);
            _active = this;
            draw.AddCallback(DrawCallback, new Ptr(callbackData), (nuint)sizeof(PanelDrawData));
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            KsaGraphicsLifetime.ObserveFailure(ex);
            Console.Error.WriteLine($"KSI2: panel blur draw unavailable: {ex.Message}");
            _failed = true;
            return false;
        }
    }

    private DescriptorPoolEx CreateMaskDescriptorPool()
    {
        VkDescriptorPoolSize poolSize = new()
        {
            Type = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = MasksPerDescriptorPool * 6
        };
        return Renderer.Device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo
        {
            MaxSets = MasksPerDescriptorPool * 2,
            PoolSizes = new Span<VkDescriptorPoolSize>(ref poolSize)
        }, null);
    }

    // ImDrawList executes callbacks after the UI drawing methods return, so
    // callback payloads must outlive Queue's stack frame.
    internal void BeginFrame()
    {
        foreach (var pointer in _pendingCallbackData)
            NativeMemory.Free((void*)pointer);
        _pendingCallbackData.Clear();
    }

    private void ReleaseCallbackData(nint pointer)
    {
        if (!_pendingCallbackData.Remove(pointer)) return;
        NativeMemory.Free((void*)pointer);
    }

    private void UpdateImages(VkImageView sceneView, VkImageView maskView, VkImageView compositionView,
        VkDescriptorSet descriptorSet)
    {
        Span<VkDescriptorImageInfo> images = stackalloc VkDescriptorImageInfo[3];
        images[0] = new VkDescriptorImageInfo
        {
            ImageView = sceneView,
            ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
            Sampler = KSA.Program.LinearClampedSampler
        };
        images[1] = new VkDescriptorImageInfo
        {
            ImageView = maskView,
            ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
            Sampler = KSA.Program.LinearClampedSampler
        };
        images[2] = new VkDescriptorImageInfo
        {
            ImageView = compositionView,
            ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
            Sampler = KSA.Program.LinearClampedSampler
        };
        Span<VkWriteDescriptorSet> writes = stackalloc VkWriteDescriptorSet[3];
        fixed (VkDescriptorImageInfo* imagePtr = images)
            for (var i = 0; i < writes.Length; i++)
                writes[i] = new VkWriteDescriptorSet
                {
                    DescriptorType = VkDescriptorType.CombinedImageSampler,
                    DstSet = descriptorSet,
                    DstBinding = i,
                    DescriptorCount = 1,
                    ImageInfo = imagePtr + i
                };
        Renderer.Device.UpdateDescriptorSets(writes, default);
    }

    private static void OnDraw(ImDrawList* list, ImDrawCmd* cmd)
    {
        var current = _active;
        if (current is null || current._disposed || cmd->UserCallbackData == 0) return;
        var callbackData = (nint)cmd->UserCallbackData;
        try
        {
            var state = (ImGuiBackendVulkanImpl.RenderState*)ImGui.GetPlatformIO().Renderer_RenderState;
            var target = KSA.Program.OffscreenTarget;
            var fallback = target?.ColorImage;
            if (state is null || fallback is null || fallback.ImageView.VkHandle != current._fallbackSceneView.VkHandle)
                return;
            var clipMin = float2.Max((cmd->ClipRect.XY - state->ClipOffset) * state->ClipScale, float2.Zero);
            var clipMax = float2.Min((cmd->ClipRect.ZW - state->ClipOffset) * state->ClipScale, state->FramebufferSize);
            if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y) return;
            VkRect2D scissor = new()
            {
                Offset = new VkOffset2D { X = (int)clipMin.X, Y = (int)clipMin.Y },
                Extent = new VkExtent2D { Width = (int)(clipMax.X - clipMin.X), Height = (int)(clipMax.Y - clipMin.Y) }
            };
            var command = state->CommandBuffer;
            command.SetScissor(0, new Span<VkRect2D>(ref scissor));
            command.BindPipeline(VkPipelineBindPoint.Graphics, current.GetPipeline(state->RenderPass));
            var data = *(PanelDrawData*)callbackData;
            VkDescriptorSet descriptor = data.Descriptor;
            var panel = data.Panel;
            if (current._process is { } process && (!process.Ready
                || process.Output?.ImageView.VkHandle != current._sceneView.VkHandle))
            {
                // Bind the valid HDR image for a solid draw; never bind an unwritten blur target.
                descriptor = data.FallbackDescriptor;
                panel.Method.X = 2;
            }
            command.BindDescriptorSets(VkPipelineBindPoint.Graphics, current.PipelineLayout, 0,
                new Span<VkDescriptorSet>(ref descriptor), default);
            panel.Scene.X = 1f / state->FramebufferSize.X;
            panel.Scene.Y = 1f / state->FramebufferSize.Y;
            command.PushConstants(current.PipelineLayout,
                VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit, ByteSize.Zero, panel);
            command.Draw(4, 1, 0, 0);
        }
        catch (Exception ex)
        {
            KsaGraphicsLifetime.ObserveFailure(ex);
            Console.Error.WriteLine($"KSI2: panel blur callback failed: {ex.Message}");
            current._failed = true;
        }
        finally
        {
            current.ReleaseCallbackData(callbackData);
        }
    }

    private VkPipeline GetPipeline(VkRenderPass pass)
    {
        if (pass.VkHandle == Renderer.MainRenderPass.VkHandle) return Pipeline;
        if (_otherPipeline.IsNull() || _otherPass.VkHandle != pass.VkHandle)
        {
            if (!_otherPipeline.IsNull()) Renderer.Device.DestroyPipeline(_otherPipeline, null);
            CreatePipeline(pass, VkPrimitiveTopology.TriangleStrip, VkCullModeFlags.None,
                VkFrontFace.CounterClockwise, VkPolygonMode.Fill,
                RenderingPresets.ReverseZDepthStencil.NoDepthTest,
                BlendState, out _otherPipeline);
            _otherPass = pass;
        }
        return _otherPipeline;
    }

    protected override VertexInput? MakeVertexInput() => null;

    private PtrOwner<VkPipelineColorBlendStateCreateInfo> BlendState =>
        _processedBlendState ?? RenderingPresets.BlendState.BlendColorAlphaOver;

    protected override void OnRebuildFrameResources()
    {
        CreatePipeline(VkPrimitiveTopology.TriangleStrip, VkCullModeFlags.None,
            VkFrontFace.CounterClockwise, VkPolygonMode.Fill,
            RenderingPresets.ReverseZDepthStencil.NoDepthTest,
            BlendState, out Pipeline);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        Release(KsaGraphicsLifetime.TryWaitIdle(Renderer.Device));
    }

    private void Release(bool canUseDevice)
    {
        if (_disposed) return;
        _disposed = true;
        KsaGraphicsLifetime.Unregister(Release);
        BeginFrame();
        if (ReferenceEquals(_active, this)) _active = null;
        if (canUseDevice)
        {
            foreach (var pool in _additionalDescriptorPools)
                Renderer.Device.DestroyDescriptorPool(pool, null);
            if (!_otherPipeline.IsNull()) Renderer.Device.DestroyPipeline(_otherPipeline, null);
            base.Dispose();
            Renderer.Device.DestroyDescriptorSetLayout(_layout, null);
        }
        _additionalDescriptorPools.Clear();
        _masks.Clear();
        _processedBlendState?.Dispose();
        _processedBlendAttachment?.Dispose();
    }
}
