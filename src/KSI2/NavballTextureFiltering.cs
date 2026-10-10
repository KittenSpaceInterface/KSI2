using Brutal;
using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA;

namespace KSI2;

// Keep the game's upload/lifetime for the image, but sample the curved artwork
// with anisotropic mip filtering instead of the shared UI bilinear sampler.
internal sealed unsafe class NavballTextureFiltering : IDisposable
{
    private VkSampler _sampler;
    private ImTextureRef? _texture;
    private bool _attempted;

    internal ImTextureRef GetTexture(TextureReference source)
    {
        if (_texture is { } texture) return texture;
        if (_attempted) return source.ImguiTextureRef!.Value;
        _attempted = true;
        var renderer = KSA.Program.GetRenderer();
        try
        {
            var settings = CreateSamplerSettings(renderer.GetFeatures().SamplerAnisotropy,
                renderer.GetLimits().MaxSamplerAnisotropy);
            _sampler = renderer.Device.CreateSampler(in settings, null);
            _texture = ImGuiBackend.Vulkan.AddTexture(_sampler, source.ImageView);
            Console.WriteLine($"KSI2: navball mip filtering enabled, anisotropy {settings.MaxAnisotropy}x.");
            return _texture.Value;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (_sampler.IsNotNull()) renderer.Device.DestroySampler(_sampler, null);
            _sampler = default;
            Console.Error.WriteLine($"KSI2: navball filtering unavailable; using UI sampler: {ex.Message}");
            return source.ImguiTextureRef!.Value;
        }
    }

    internal static VkSamplerCreateInfo CreateSamplerSettings(bool supportsAnisotropy, float maxAnisotropy)
    {
        var settings = Presets.Sampler.SamplerLinearClamped;
        settings.AnisotropyEnable = supportsAnisotropy;
        settings.MaxAnisotropy = supportsAnisotropy ? Math.Clamp(maxAnisotropy, 1f, 8f) : 1f;
        return settings;
    }

    public void Dispose()
    {
        if (_texture is null && !_sampler.IsNotNull()) return;
        var device = KSA.Program.GetRenderer().Device;
        // Submitted ImGui frames can still reference our descriptor and sampler.
        device.WaitIdle();
        if (_texture is { } texture) ImGuiBackend.Vulkan.RemoveTexture(texture);
        if (_sampler.IsNotNull()) device.DestroySampler(_sampler, null);
        _texture = null;
        _sampler = default;
    }
}
