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
    private readonly Dictionary<TextureReference, ImTextureRef> _textures = new();
    private bool _unavailable;
    private bool _disposed;
    private Device? _device;

    internal ImTextureRef GetTexture(TextureReference source)
    {
        if (_textures.TryGetValue(source, out var texture)) return texture;
        if (_unavailable || _disposed) return source.ImguiTextureRef!.Value;
        var renderer = KSA.Program.GetRenderer();
        try
        {
            if (!_sampler.IsNotNull())
            {
                KsaGraphicsLifetime.EnsureInstalled(renderer.Device);
                var settings = CreateSamplerSettings(renderer.GetFeatures().SamplerAnisotropy,
                    renderer.GetLimits().MaxSamplerAnisotropy);
                _sampler = renderer.Device.CreateSampler(in settings, null);
                _device = renderer.Device;
                KsaGraphicsLifetime.Register(Release);
                Console.WriteLine($"KSI2: navball mip filtering enabled, anisotropy {settings.MaxAnisotropy}x.");
            }
            texture = ImGuiBackend.Vulkan.AddTexture(_sampler, source.ImageView);
            _textures.Add(source, texture);
            return texture;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            KsaGraphicsLifetime.ObserveFailure(ex);
            _unavailable = true;
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
        if (_disposed) return;
        Release(_device is not null && KsaGraphicsLifetime.TryWaitIdle(_device));
    }

    private void Release(bool canUseDevice)
    {
        if (_disposed) return;
        _disposed = true;
        KsaGraphicsLifetime.Unregister(Release);
        if (canUseDevice && _device is not null)
        {
            foreach (var texture in _textures.Values) ImGuiBackend.Vulkan.RemoveTexture(texture);
            if (_sampler.IsNotNull()) _device.DestroySampler(_sampler, null);
        }
        _textures.Clear();
        _sampler = default;
        _device = null;
    }
}
