using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using KSA;

namespace KSI2;

internal sealed class ReadoutArtworkFiltering : IDisposable
{
    private readonly Dictionary<TextureReference, ImTextureRef> _pointTextures = new();
    private bool _unavailable;

    internal ImTextureRef GetPointTexture(TextureReference source)
    {
        if (_pointTextures.TryGetValue(source, out var texture)) return texture;
        if (_unavailable) return source.ImguiTextureRef!.Value;
        try
        {
            texture = ImGuiBackend.Vulkan.AddTexture(KSA.Program.PointClampedSampler, source.ImageView);
            _pointTextures.Add(source, texture);
            return texture;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _unavailable = true;
            Console.Error.WriteLine($"KSI2: readout point filtering unavailable: {ex.Message}");
            return source.ImguiTextureRef!.Value;
        }
    }

    public void Dispose()
    {
        if (_pointTextures.Count == 0) return;
        KSA.Program.GetRenderer().Device.WaitIdle();
        foreach (var texture in _pointTextures.Values) ImGuiBackend.Vulkan.RemoveTexture(texture);
        _pointTextures.Clear();
    }
}
