using KSA;

namespace KSI2;

internal sealed class TextureRegistry
{
    internal void Register(string id, Action<TextureReference> assign)
    {
        try
        {
            var texture = ModLibrary.Get<TextureReference>(id);
            texture.AddToImgui();
            assign(texture);
            Console.WriteLine($"KSI2: registered '{id}' through KSA texture pipeline.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"KSI2: could not register '{id}': {ex.Message}");
        }
    }
}
