using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA;
using KSA.Rendering;
using RenderCore;

namespace KSI2;

// Records into KSA's command buffer before its presentation render pass begins.
internal sealed unsafe class PanelBlurPass : RenderTechnique
{
    private readonly DescriptorSetLayoutEx _layout;
    private readonly VkDescriptorSet _set;
    private readonly RenderTarget _target;

    internal PanelBlurPass(RendererContext context, RenderTarget target, VkImageView source)
        : base("KSI2 Gaussian blur pass", context, target,
            [ModLibrary.Get<ShaderReference>("KSI2BlurPassVert"),
             ModLibrary.Get<ShaderReference>("KSI2BlurPassFrag")])
    {
        _target = target;
        try
        {
            VkDescriptorPoolSize size = new() { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = 1 };
            DescriptorPool = Renderer.Device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo
            {
                MaxSets = 1, PoolSizes = new Span<VkDescriptorPoolSize>(ref size)
            }, null);
            VkDescriptorSetLayoutBinding binding = new()
            {
                Binding = 0, DescriptorType = VkDescriptorType.CombinedImageSampler,
                DescriptorCount = 1, StageFlags = VkShaderStageFlags.FragmentBit
            };
            _layout = Renderer.Device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo
            {
                Bindings = new Span<VkDescriptorSetLayoutBinding>(ref binding)
            }, null);
            _set = Renderer.Device.AllocateDescriptorSet(DescriptorPool, _layout);
            VkDescriptorImageInfo image = new()
            {
                ImageView = source, ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
                Sampler = KSA.Program.LinearClampedSampler
            };
            VkWriteDescriptorSet write = new()
            {
                DstSet = _set, DstBinding = 0, DescriptorCount = 1,
                DescriptorType = VkDescriptorType.CombinedImageSampler, ImageInfo = &image
            };
            Renderer.Device.UpdateDescriptorSets(new Span<VkWriteDescriptorSet>(ref write), default);
            VkDescriptorSetLayout layout = _layout;
            VkPushConstantRange range = new()
            {
                StageFlags = VkShaderStageFlags.FragmentBit, Size = ByteSize.Of<float4>()
            };
            PipelineLayout = Renderer.Device.CreatePipelineLayout(new Span<VkDescriptorSetLayout>(ref layout),
                new Span<VkPushConstantRange>(ref range), null);
            RebuildFrameResources();
        }
        catch
        {
            base.Dispose();
            if (_layout is not null) Renderer.Device.DestroyDescriptorSetLayout(_layout, null);
            throw;
        }
    }

    internal void Record(CommandBuffer command, float2 axis)
    {
        _target.BeginRendering(command, default, default);
        try
        {
            VkViewport viewport = new()
            {
                Width = _target.Extent.Width, Height = _target.Extent.Height, MinDepth = 0, MaxDepth = 1
            };
            VkRect2D scissor = new(_target.Extent);
            command.SetViewport(0, new Span<VkViewport>(ref viewport));
            command.SetScissor(0, new Span<VkRect2D>(ref scissor));
            command.BindPipeline(VkPipelineBindPoint.Graphics, Pipeline);
            var set = _set;
            command.BindDescriptorSets(VkPipelineBindPoint.Graphics, PipelineLayout, 0,
                new Span<VkDescriptorSet>(ref set), default);
            command.PushConstants(PipelineLayout, VkShaderStageFlags.FragmentBit, ByteSize.Zero,
                new float4(axis.X, axis.Y, _target.Extent.Width, _target.Extent.Height));
            command.Draw(4, 1, 0, 0);
        }
        finally { _target.EndRendering(command); }
    }

    protected override VertexInput? MakeVertexInput() => null;
    protected override void OnRebuildFrameResources()
        => CreatePipeline(VkPrimitiveTopology.TriangleStrip, VkCullModeFlags.None,
            VkFrontFace.CounterClockwise, VkPolygonMode.Fill,
            RenderingPresets.ReverseZDepthStencil.NoDepthTest,
            RenderingPresets.BlendState.BlendColorAlphaOver, out Pipeline);

    public override void Dispose()
    {
        base.Dispose();
        Renderer.Device.DestroyDescriptorSetLayout(_layout, null);
    }
}
