using KSA;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSI2;

internal static class FlightUiSounds
{
    private static readonly HashSet<string> ReportedAudioRoutes = new(StringComparer.Ordinal);

    internal static void Play(string id) => Play(id, ImGui.GetMousePos());

    internal static void Play(string id, float2 screenCenter)
    {
        if (SourceUiSounds.Supports(id) && PilotSettingsController.SourceUiAudioEnabled)
        {
            try
            {
                ReportAudioRoute(id, SourceUiSounds.TryPlay(id, screenCenter)
                    ? "source renderer" : "source unavailable; no sound played");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"KSI2: source playback for '{id}' failed: {ex.Message}");
                ReportAudioRoute(id, "source failed; no sound played");
            }
            return;
        }
        try
        {
            GameAudio.Play2DSound(ModLibrary.Get<SoundBehavior>(id), out _, 1f);
            if (SourceUiSounds.Supports(id)) ReportAudioRoute(id, "recorded sound");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"KSI2: could not play '{id}': {ex.Message}");
        }
    }

    private static void ReportAudioRoute(string id, string route)
    {
        var state = Universe.IsPaused() ? "paused" : "running";
        if (!ReportedAudioRoutes.Add($"{id}|{state}|{route}")) return;
        var message = $"{DateTimeOffset.Now:O} KSI2 audio: {id}, {state}, {route}";
        Console.WriteLine(message);
        try
        {
            var directory = Path.Combine(KSA.Constants.DocumentsFolderPath, "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "KSI2-audio.log"), message + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"KSI2: could not write audio route log: {ex.Message}");
        }
    }
}
