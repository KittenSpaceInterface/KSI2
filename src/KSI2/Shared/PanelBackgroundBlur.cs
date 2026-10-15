using Brutal;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Extensions;
using Brutal.Numerics;
using Brutal.Pointers;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

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
    }

    private struct PanelDrawData
    {
        public PanelData Panel;
        public VkDescriptorSet Descriptor;
    }

    private sealed class MaskDescriptor
    {
        public required VkImageView View;
        public required VkDescriptorSet Set;
    }

    private const int MaxPanelMasks = 5;

    private static readonly ImDrawCallback DrawCallback = OnDraw;
    private static PanelBackgroundBlur? _active;
    private readonly DescriptorSetLayoutEx _layout;
    private readonly Dictionary<TextureReference, MaskDescriptor> _masks = new();
    private VkImageView _sceneView;
    private VkRenderPass _otherPass;
    private VkPipeline _otherPipeline;
    private bool _disposed;
    private bool _failed;

    private PanelBackgroundBlur(RendererContext context)
        : base("KSI2 panel background blur", context, context.RenderPassInfo,
            [ModLibrary.Get<ShaderReference>("KSI2PanelBlurVert"),
             ModLibrary.Get<ShaderReference>("KSI2PanelBlurFrag")])
    {
        var device = context.Renderer.Device;
        VkDescriptorPoolSize poolSize = new()
        {
            Type = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = MaxPanelMasks * 2
        };
        DescriptorPool = device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo
        {
            MaxSets = MaxPanelMasks,
            PoolSizes = new Span<VkDescriptorPoolSize>(ref poolSize)
        }, null);
        Span<VkDescriptorSetLayoutBinding> bindings = stackalloc VkDescriptorSetLayoutBinding[2];
        for (var i = 0; i < 2; i++)
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
    }

    internal static PanelBackgroundBlur? Create()
    {
        try
        {
            if (ModLibrary.Get<ShaderReference>("KSI2PanelBlurVert").Shader is null ||
                ModLibrary.Get<ShaderReference>("KSI2PanelBlurFrag").Shader is null)
                throw new InvalidOperationException("Blur shaders were not loaded.");
            return new PanelBackgroundBlur(KSA.Program.GetGaugeContext());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"KSI2: panel blur unavailable: {ex}");
            return null;
        }
    }

    internal bool Queue(ImDrawListPtr draw, float2 min, float2 max, TextureReference mask,
        float opacity, bool flipVertically = false, PanelMaskNineSlice? maskNineSlice = null,
        bool rectangularMask = false)
    {
        try
        {
            if (_failed) return false;
            var scene = KSA.Program.OffscreenTarget?.ColorImage;
            if (scene is null) return false;
            if (scene.Width <= 0 || scene.Height <= 0 || mask.ImageView.IsNull()) return false;
            if (_sceneView.VkHandle != scene.ImageView.VkHandle)
            {
                // Descriptor changes must not race the prior frame's command buffer.
                Renderer.Device.WaitIdle();
                _sceneView = scene.ImageView;
                foreach (var entry in _masks.Values)
                    UpdateImages(_sceneView, entry.View, entry.Set);
            }
            if (!_masks.TryGetValue(mask, out var maskDescriptor))
            {
                if (_masks.Count >= MaxPanelMasks) throw new InvalidOperationException("Too many panel masks.");
                maskDescriptor = new MaskDescriptor
                {
                    View = mask.ImageView,
                    Set = Renderer.Device.AllocateDescriptorSet(DescriptorPool, _layout)
                };
                UpdateImages(_sceneView, maskDescriptor.View, maskDescriptor.Set);
                _masks.Add(mask, maskDescriptor);
            }
            else if (maskDescriptor.View.VkHandle != mask.ImageView.VkHandle)
            {
                Renderer.Device.WaitIdle();
                maskDescriptor.View = mask.ImageView;
                UpdateImages(_sceneView, maskDescriptor.View, maskDescriptor.Set);
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
                        : float4.Zero
                },
                Descriptor = maskDescriptor.Set
            };
            _active = this;
            draw.AddCallback(DrawCallback, new Ptr(&data), (nuint)sizeof(PanelDrawData));
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"KSI2: panel blur draw unavailable: {ex.Message}");
            _failed = true;
            return false;
        }
    }

    private void UpdateImages(VkImageView sceneView, VkImageView maskView, VkDescriptorSet descriptorSet)
    {
        Span<VkDescriptorImageInfo> images = stackalloc VkDescriptorImageInfo[2];
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
        Span<VkWriteDescriptorSet> writes = stackalloc VkWriteDescriptorSet[2];
        fixed (VkDescriptorImageInfo* imagePtr = images)
            for (var i = 0; i < 2; i++)
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
        try
        {
            var state = (ImGuiBackendVulkanImpl.RenderState*)ImGui.GetPlatformIO().Renderer_RenderState;
            var target = KSA.Program.OffscreenTarget;
            var scene = target?.ColorImage;
            if (state is null || scene is null || scene.ImageView.VkHandle != current._sceneView.VkHandle)
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
            var data = *(PanelDrawData*)cmd->UserCallbackData;
            VkDescriptorSet descriptor = data.Descriptor;
            command.BindDescriptorSets(VkPipelineBindPoint.Graphics, current.PipelineLayout, 0,
                new Span<VkDescriptorSet>(ref descriptor), default);
            var panel = data.Panel;
            panel.Scene.X = 1f / state->FramebufferSize.X;
            panel.Scene.Y = 1f / state->FramebufferSize.Y;
            command.PushConstants(current.PipelineLayout,
                VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit, ByteSize.Zero, panel);
            command.Draw(4, 1, 0, 0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"KSI2: panel blur callback failed: {ex.Message}");
            current._failed = true;
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
                RenderingPresets.BlendState.BlendColorAlphaOver, out _otherPipeline);
            _otherPass = pass;
        }
        return _otherPipeline;
    }

    protected override VertexInput? MakeVertexInput() => null;

    protected override void OnRebuildFrameResources()
    {
        CreatePipeline(VkPrimitiveTopology.TriangleStrip, VkCullModeFlags.None,
            VkFrontFace.CounterClockwise, VkPolygonMode.Fill,
            RenderingPresets.ReverseZDepthStencil.NoDepthTest,
            RenderingPresets.BlendState.BlendColorAlphaOver, out Pipeline);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Renderer.Device.WaitIdle();
        if (!_otherPipeline.IsNull()) Renderer.Device.DestroyPipeline(_otherPipeline, null);
        base.Dispose();
        Renderer.Device.DestroyDescriptorSetLayout(_layout, null);
        if (ReferenceEquals(_active, this)) _active = null;
    }
}
