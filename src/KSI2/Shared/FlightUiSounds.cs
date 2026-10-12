using KSA;

namespace KSI2;

internal static class FlightUiSounds
{
    internal static void Play(string id)
    {
        try
        {
            GameAudio.Play2DSound(ModLibrary.Get<SoundBehavior>(id), out _, 1f);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"KSI2: could not play '{id}': {ex.Message}");
        }
    }
}
