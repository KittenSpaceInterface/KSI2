using Brutal.FmodApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSI2;

// Plays the studied KSP2 UI source graphs on KSA's existing FMOD system.
// Voice data and gain paths are from the inspected Wwise UI bank. The shared
// -15 dB conversion is the measured Wwise-to-FMOD source-level conversion.
internal static unsafe class SourceUiSounds
{
    private const float SourceLevelTransferDb = -15f;
    private const int OscillatorType = 0;
    private const int OscillatorRate = 1;
    private const int TremoloFrequency = 0;
    private const int TremoloDepth = 1;
    private const int TremoloShape = 2;
    private const int TremoloPhase = 6;

    private sealed record Voice(float Frequency, float Duration, double Delay, float BankGainDb);
    private sealed record Profile(string? SourceId, float SourcePitch, float? SourceBankGainDb,
        Voice[] Sines, bool ToggleCurve, bool Tremolo, float DefaultX, float DefaultY,
        bool UsePosition = true);

    private static readonly float[] WarpHoverPitchCents =
        [1500f, 1700f, 1800f, 2000f, 2200f, 2400f, 2600f, 2700f, 2900f, 3100f, 3300f];

    private sealed class Playback
    {
        internal ChannelGroup Group;
        internal readonly List<Channel> Channels = [];
        internal readonly List<Dsp> Dsps = [];
        internal long ReleaseAfter;

        internal void Release()
        {
            foreach (var channel in Channels) channel.TryStop();
            foreach (var dsp in Dsps) dsp.TryRelease();
            if (!Group.IsNull()) Group.TryRelease();
        }
    }

    private static readonly List<Playback> Playing = [];
    private static readonly Voice[] OnSines =
    [
        new(150f, .1f, 0, -8f),
        new(224.746f, .066742f, .08, -8f),
        new(252.269f, .059460f, .16, -8f)
    ];
    private static readonly Voice[] OffSines =
    [
        new(397.726f, .037714f, .09, -8f),
        new(334.446f, .044850f, .15, -8f),
        new(292.839f, .051223f, .21, -8f)
    ];

    private static Profile? GetProfile(string id) => id switch
    {
        "KSI2SasRcsEnable" => new("KSI2UiSourceSasRcs", .954842f, -4f,
            OnSines, true, true, .1474f, .1315f),
        "KSI2SasRcsDisable" => new("KSI2UiSourceSasRcs", MathF.Pow(2f, -80f / 1200f), -4f,
            OffSines, true, true, .1474f, .1315f),
        "KSI2SasRcsHover" => new(null, 1f, null,
            [new(150f * MathF.Pow(2f, 300f / 1200f), .1f / MathF.Pow(2f, 300f / 1200f), 0, -9f)],
            false, false, .1474f, .1315f),
        "KSI2AltitudeModeHover" => new(null, 1f, null,
            [new(150f * MathF.Pow(2f, 800f / 1200f), .1f / MathF.Pow(2f, 800f / 1200f), 0, -9f)],
            false, false, .2021f, .2148f),
        "KSI2SpeedModeHover" => new(null, 1f, null,
            [new(150f * MathF.Pow(2f, 300f / 1200f), .1f / MathF.Pow(2f, 300f / 1200f), 0, -9f)],
            false, false, .0745f, .2148f),
        "KSI2AltitudeModeSelect" => ClickProfile(true),
        "KSI2SpeedModeSelect" => ClickProfile(false),
        "KSI2TimePauseHover" => new("KSI2UiSourceTimePauseHover",
            MathF.Pow(2f, 3400f / 1200f), 1f, [], false, false, 0f, 0f, false),
        "KSI2TimeModeHover" => new(null, 1f, null,
            [Sine(150f, .1f, 300f, 0, -15f)], false, false, .5f, .5f),
        "KSI2TimeUtToMet" => ClickProfile(false) with { DefaultX = .5f, DefaultY = .5f },
        "KSI2TimeMetToUt" => new("KSI2UiSourceExtendedClick",
            MathF.Pow(2f, 540f / 1200f), -4f,
            [Sine(125f, .2f, 1200f, 0, -15f),
             Sine(100f, .1f, 1700f, .08, -11f)],
            false, false, .5f, .5f),
        "KSI2TimeWarpChange" => WarpClick(300f, -3f),
        "KSI2TimeWarpReturnToOne" => WarpClick(0f, 1f),
        _ when TryWarpHoverIndex(id, out var index) => WarpHover(index),
        _ => null
    };

    private static Voice Sine(float frequency, float duration, float cents,
        double delay, float bankGainDb)
    {
        var rate = MathF.Pow(2f, cents / 1200f);
        return new(frequency * rate, duration / rate, delay, bankGainDb);
    }

    private static Profile WarpClick(float cents, float bankGainDb) =>
        new("KSI2UiSourceTimeWarpClick", MathF.Pow(2f, cents / 1200f),
            bankGainDb, [], false, false, 0f, 0f, false);

    private static Profile WarpHover(int index) =>
        new(null, 1f, null,
            [Sine(440f, .05f, WarpHoverPitchCents[index], .09, -17f)],
            false, false, 0f, 0f, false);

    private static bool TryWarpHoverIndex(string id, out int index)
    {
        index = -1;
        const string prefix = "KSI2TimeWarpHover";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(id.AsSpan(prefix.Length), out var number)
            || number is < 1 or > 11)
            return false;
        index = number - 1;
        return true;
    }

    private static Profile ClickProfile(bool altitude)
    {
        var firstCents = altitude ? 2600f : 2200f;
        var secondCents = altitude ? 3300f : 2900f;
        var firstRate = MathF.Pow(2f, firstCents / 1200f);
        var secondRate = MathF.Pow(2f, secondCents / 1200f);
        return new("KSI2UiSourceExtendedClick",
            MathF.Pow(2f, (altitude ? 1940f : 1540f) / 1200f),
            altitude ? 0f : 6f,
            [new(125f * firstRate, .2f / firstRate, 0, altitude ? -21f : -15f),
             new(100f * secondRate, .1f / secondRate, .08, altitude ? -17f : -11f)],
            false, false, altitude ? .2021f : .0745f, .2148f);
    }

    internal static bool Supports(string id) => GetProfile(id) is not null;

    internal static bool TryPlay(string id, float2? screenCenter = null)
    {
        var profile = GetProfile(id);
        if (profile is null) return false;
        var x = profile.DefaultX;
        var y = profile.DefaultY;
        if (screenCenter is { } point)
        {
            var display = ImGui.GetIO().DisplaySize;
            if (display.X > 0 && display.Y > 0)
            {
                x = Math.Clamp(point.X / display.X, 0f, 1f);
                y = Math.Clamp(1f - point.Y / display.Y, 0f, 1f);
            }
        }
        return TryPlayAtPosition(id, x, y, Universe.IsPaused());
    }

    // The same runtime path used by controls, with explicit RTPC coordinates.
    // Kept separate so the source/effects behavior can be checked offline.
    internal static bool TryPlayAtPosition(string id, float x, float y, bool paused)
    {
        var profile = GetProfile(id);
        if (profile is null) return false;
        // Resolve source media through a registered asset. No package path or
        // separate investigation files are needed by the installed code mod.
        Sound source = default;
        if (profile.SourceId is not null)
        {
            if (ModLibrary.Get<SoundBehavior>(profile.SourceId) is not SoundReference reference
                || reference.SoundFiles.Count != 1)
                return false;
            source = reference.SoundFiles[0].FmodSound;
            if (source.IsNull()) return false;
        }

        var system = GameAudio.System;
        if (system.IsNull()) return false;
        var pitch = profile.UsePosition ? PositionPitch(y, profile.ToggleCurve) : 1f;
        var groupPitch = paused ? .5f : 1f;
        var pan = profile.UsePosition ? Math.Clamp(x * .25f, -1f, 1f) : 0f;
        var playback = new Playback();
        try
        {
            Require(system.TryGetSoftwareFormat(out var sampleRate, out _, out _), "read audio rate");
            Require(system.TryGetDspBufferSize(out var bufferFrames, out _),
                "read audio buffer size");
            Require(GameAudio.CreateFmodChannelGroup("KSI2 source UI", out playback.Group,
                out var groupResult) ? Result.Ok : groupResult, "create UI source group");
            var uiGroup = GameAudio.GetChannelGroup(ChannelGroupType.Ui);
            DspConnection connection = default;
            Require(Fmod.PInvoke.ChannelGroupAddGroup(uiGroup, playback.Group, false, &connection),
                "route UI source group");
            Require(playback.Group.TrySetPitch(groupPitch), "set paused UI pitch");

            var lastEnd = 0d;
            if (!source.IsNull())
            {
                Require(source.TryGetLength(out var sourceFrames, Brutal.FmodApi.TimeUnit.Pcm),
                    "read source length");
                var sourcePitch = pitch * profile.SourcePitch;
                PlaySource(system, source, playback, sourcePitch,
                    profile.SourceBankGainDb!.Value + SourceLevelTransferDb,
                    groupPitch, bufferFrames);
                lastEnd = sourceFrames / (double)sampleRate / (sourcePitch * groupPitch);
            }
            for (var i = 0; i < profile.Sines.Length; i++)
            {
                var voice = profile.Sines[i];
                var rate = pitch;
                var frequency = voice.Frequency;
                var duration = voice.Duration;
                if (id == "KSI2SasRcsDisable")
                {
                    var referencePitch = MathF.Pow(2f, 958.2f / 1200f);
                    frequency /= referencePitch;
                    duration *= referencePitch;
                }
                var voiceEnd = PlaySine(system, playback, frequency * rate,
                    voice.Delay, duration / rate, voice.BankGainDb + SourceLevelTransferDb,
                    pan, profile.Tremolo, i, paused, sampleRate, bufferFrames);
                lastEnd = Math.Max(lastEnd, voiceEnd);
            }
            playback.ReleaseAfter = Environment.TickCount64 + (long)Math.Ceiling((lastEnd + .5) * 1000);
            Playing.Add(playback);
            return true;
        }
        catch (Exception error)
        {
            playback.Release();
            Console.Error.WriteLine($"KSI2: source UI audio for '{id}' failed: {error.Message}");
            return false;
        }
    }

    private static void PlaySource(FmodSystem system, Sound source, Playback playback,
        float pitch, float gainDb, float groupPitch, uint bufferFrames)
    {
        Require(system.TryPlaySound(source, playback.Group, true, out var channel), "start source voice");
        playback.Channels.Add(channel);
        Require(channel.TrySetPitch(pitch), "set source pitch");
        Require(channel.TrySetVolume(MathF.Pow(10f, gainDb / 20f)), "set source gain");
        Require(channel.TryGetDspClock(out _, out var parentClock), "read source clock");
        Require(channel.TrySetDelay(parentClock + (ulong)Math.Round(bufferFrames * groupPitch),
            0, false),
            "schedule source voice");
        Require(channel.TrySetPaused(false), "play source voice");
    }

    private static double PlaySine(FmodSystem system, Playback playback, float frequency,
        double delay, float duration, float gainDb, float pan, bool tremolo,
        int index, bool paused, int sampleRate, uint bufferFrames)
    {
        Require(system.TryCreateDspByType(DspType.Oscillator, out var oscillator), "create oscillator");
        playback.Dsps.Add(oscillator);
        Require(oscillator.TrySetParameterInt(OscillatorType, 0), "select sine wave");
        Require(oscillator.TrySetParameterFloat(OscillatorRate, frequency), "set sine frequency");
        Require(system.TryPlayDsp(oscillator, playback.Group, true, out var channel), "start sine voice");
        playback.Channels.Add(channel);
        Require(channel.TrySetVolume(MathF.Pow(10f, gainDb / 20f)), "set sine gain");
        var matrix = new[] { MathF.Sqrt((1f - pan) * .5f), MathF.Sqrt((1f + pan) * .5f) };
        fixed (float* pointer = matrix)
            Require(channel.TrySetMixMatrix(pointer, 2, 1, 1), "set sine pan");
        if (tremolo)
        {
            Require(system.TryCreateDspByType(DspType.Tremolo, out var lfo), "create tremolo");
            playback.Dsps.Add(lfo);
            Require(lfo.TrySetParameterFloat(TremoloFrequency, 15.58f), "set tremolo rate");
            Require(lfo.TrySetParameterFloat(TremoloDepth, .363f), "set tremolo depth");
            Require(lfo.TrySetParameterFloat(TremoloShape, 1f), "set tremolo shape");
            Require(lfo.TrySetParameterFloat(TremoloPhase,
                (float)((.1875 + delay * (paused ? .5 : 1) * 15.58) % 1)), "set tremolo phase");
            Require(channel.TryAddDsp(0, lfo), "insert tremolo");
        }
        Require(channel.TryGetDspClock(out _, out var parentClock), "read sine clock");
        var groupPitch = paused ? .5f : 1f;
        var start = parentClock + (ulong)Math.Round((bufferFrames + delay * sampleRate) * groupPitch);
        if (paused) duration = Math.Max(0f, duration - .005f);
        var attackOffsetMs = index < 2 ? -4f : 0f;
        var audibleStart = (ulong)Math.Max(0L,
            (long)start + (long)Math.Round(attackOffsetMs * sampleRate / 1000f));
        var end = audibleStart + (ulong)Math.Round(duration * sampleRate);
        var release = end + (ulong)Math.Round((index == 1 ? .008 : .010) * sampleRate)
            + (ulong)Math.Round(.004 * sampleRate);
        Require(channel.TrySetDelay(start, release, false), "schedule sine voice");
        Require(channel.TryAddFadePoint(start, 0f), "start sine envelope");
        Require(channel.TryAddFadePoint(audibleStart, 0f), "start sine attack");
        Require(channel.TryAddFadePoint(audibleStart + (ulong)Math.Round(.008 * sampleRate), 1f),
            "fade in sine");
        Require(channel.TryAddFadePoint(end, 1f), "end sine sustain");
        Require(channel.TryAddFadePoint(release, 0f), "fade out sine");
        Require(channel.TrySetPaused(false), "play sine voice");
        return delay + (duration + .014) / groupPitch;
    }

    private static float PositionPitch(float y, bool toggleCurve)
    {
        var t = Math.Clamp((y + 1f) * .5f, 0f, 1f);
        var cents = toggleCurve ? -1073f + t * 3595f : -1000f + t * 3500f;
        return MathF.Pow(2f, cents / 1200f);
    }

    internal static void Update()
    {
        for (var i = Playing.Count - 1; i >= 0; i--)
        {
            if (Environment.TickCount64 < Playing[i].ReleaseAfter) continue;
            Playing[i].Release();
            Playing.RemoveAt(i);
        }
    }

    internal static void Dispose()
    {
        foreach (var playback in Playing) playback.Release();
        Playing.Clear();
    }

    private static void Require(Result result, string operation)
    {
        if (result != Result.Ok)
            throw new InvalidOperationException($"FMOD could not {operation}: {result}");
    }
}
