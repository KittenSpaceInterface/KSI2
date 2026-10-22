using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using KSI2;

internal static class PanelBackgroundChecks
{
    internal static void Run()
    {
        CheckTimeBackgroundGeometry();
        var old = PilotUiSettings.Parse("""{"BlurPanelBackgrounds":true,"BlurOpacityTicks":240}""");
        if (old.EffectiveBackgroundStyle != PanelBackgroundStyle.CurrentBlur
            || old.BlurOpacityTicks != 240 || old.Ksp2BlurOpacityTicks != 400
            || JsonSerializer.Serialize(old).Contains("BlurPanelBackgrounds"))
            throw new Exception("Legacy background migration lost the selected mode or opacity.");
        if (PilotUiSettings.Parse("""{"BlurPanelBackgrounds":false}""").EffectiveBackgroundStyle != PanelBackgroundStyle.Solid
            || PilotUiSettings.Parse("""{"BackgroundStyle":2,"BlurPanelBackgrounds":true}""").EffectiveBackgroundStyle != PanelBackgroundStyle.Ksp2Blur
            || PilotUiSettings.Parse("""{"BackgroundStyle":999,"BlurPanelBackgrounds":true}""").EffectiveBackgroundStyle != PanelBackgroundStyle.Solid)
            throw new Exception("Background mode precedence or unknown-mode recovery failed.");
        var roundTrip = PilotUiSettings.Parse(JsonSerializer.Serialize(new PilotUiSettings
        {
            BackgroundStyle = PanelBackgroundStyle.Ksp2Blur, BlurOpacityTicks = 240, Ksp2BlurOpacityTicks = 360
        }));
        if (roundTrip.EffectiveBackgroundStyle != PanelBackgroundStyle.Ksp2Blur
            || roundTrip.BlurOpacityTicks != 240 || roundTrip.Ksp2BlurOpacityTicks != 360)
            throw new Exception("Blur modes did not retain independent opacity settings.");
        var marker = Path.Combine(Path.GetTempPath(), $"ksi2-blur-test-{Guid.NewGuid():N}");
        try
        {
            var settings = new PilotUiSettings { BlurPanelBackgrounds = true };
            var guard = new BlurCrashGuard(settings, () => true, marker);
            if (!File.Exists(marker) || !guard.Enabled) throw new Exception("Activation marker was not armed.");
            guard.OnCleanUnload();
            if (File.Exists(marker)) throw new Exception("Clean unload retained the marker.");
            guard.SetStyle(PanelBackgroundStyle.Ksp2Blur);
            if (!File.Exists(marker) || guard.Style != PanelBackgroundStyle.Ksp2Blur)
                throw new Exception("Switching between GPU modes did not rearm recovery.");
            guard.SetStyle(PanelBackgroundStyle.Solid);
            if (File.Exists(marker) || guard.Enabled) throw new Exception("Solid style retained the marker.");
            guard.SetStyle(PanelBackgroundStyle.Ksp2Blur);
            var recovered = new BlurCrashGuard(settings, () => true, marker);
            if (recovered.Enabled || !recovered.RecoveredAfterCrash || File.Exists(marker))
                throw new Exception("Unclean activation did not recover to solid style.");
            var cannotSave = new BlurCrashGuard(settings, () => false, marker);
            cannotSave.SetStyle(PanelBackgroundStyle.Ksp2Blur);
            if (cannotSave.Enabled || cannotSave.Error is null)
                throw new Exception("Failed settings save activated a GPU mode.");
        }
        finally { if (File.Exists(marker)) File.Delete(marker); }

        var dir = Path.Combine(AppContext.BaseDirectory, "assets", "optional");
        OptionalAssemblyLoader.Load(Path.Combine(dir, "0Harmony.dll"));
        var assembly = OptionalAssemblyLoader.Load(Path.Combine(dir, "KSI2.FlightComputerInputPatch.dll"));
        CheckGraphicsLifetimePatch(assembly);
        var patch = assembly.GetType("KSI2.Optional.HarmonyPanelBlurPatch", true)!;
        var probe = new Probe();
        var command = new CommandProbe(17);
        object? observed = null;
        var target = typeof(Probe).GetMethod("Prepare", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            patch.GetMethod("Install")!.Invoke(null,
                [target, (Action<object, object, int>)((instance, cmd, frame) =>
                {
                    if (!ReferenceEquals(instance, probe) || frame != 2)
                        throw new Exception("Render hook changed its arguments.");
                    observed = cmd;
                })]);
            probe.Run(command, 2);
            if (observed is not CommandProbe { Value: 17 })
                throw new Exception("Render hook failed to box and forward the command buffer struct.");
        }
        finally { patch.GetMethod("Uninstall")!.Invoke(null, null); }
        observed = null;
        probe.Run(command, 2);
        if (observed is not null) throw new Exception("Render hook remained installed after unload.");
        if (Environment.GetEnvironmentVariable("KSA_REFERENCE_PATH") is { Length: > 0 } gameDir)
            InspectInstalledHook(patch, gameDir);
        Console.WriteLine("Background migration, opacity, crash recovery and Harmony forwarding checks passed.");
    }

    private static void CheckTimeBackgroundGeometry()
    {
        // Disjoint scissor regions must share one snapped edge at every HUD scale.
        foreach (var scale in new[] { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f })
        foreach (var origin in new[] { 0f, 0.25f, 20.5f })
        {
            var left = MathF.Round(origin);
            var split = MathF.Round(origin + TimeSelectorGeometry.BackgroundSplit * scale);
            var capRight = MathF.Round(origin + TimeSelectorGeometry.RoundedEndSize * scale);
            var right = MathF.Round(origin + TimeSelectorGeometry.PanelWidth * scale);
            if (split <= left || split >= capRight || capRight >= right)
                throw new Exception("Time background split no longer fits inside the original rounded cap.");
            for (var pixel = (int)left; pixel < right; pixel++)
            {
                var center = pixel + 0.5f;
                var cap = center >= left && center < split && center < capRight;
                var body = center >= split && center < right;
                if (cap == body)
                    throw new Exception("Time background regions overlap or leave an uncovered pixel.");
            }
        }
        var sourcePixelsPerUnit = TimeSelectorGeometry.RoundedEndUvWidth * 704f
            / TimeSelectorGeometry.RoundedEndSize;
        if (MathF.Abs(sourcePixelsPerUnit - 2f) > 0.00001f)
            throw new Exception("Time rounded cap no longer preserves its original 2x artwork mapping.");
        Console.WriteLine("Time background regions preserve the rounded cap and cover each pixel once.");
    }

    private static void CheckGraphicsLifetimePatch(Assembly assembly)
    {
        var patch = assembly.GetType("KSI2.Optional.HarmonyGraphicsLifetimePatch", true)!;
        var order = new List<string>();
        var device = new DeviceLifetimeProbe(order);
        var program = new ProgramLifetimeProbe(order, device);
        try
        {
            patch.GetMethod("Install")!.Invoke(null,
                [typeof(ProgramLifetimeProbe).GetMethod(nameof(ProgramLifetimeProbe.Dispose))!,
                 typeof(DeviceLifetimeProbe).GetMethod(nameof(DeviceLifetimeProbe.Dispose))!,
                 (Action<object>)(instance =>
                 {
                     if (!ReferenceEquals(instance, program) || device.Disposed)
                         throw new Exception("Graphics cleanup ran after device destruction.");
                     order.Add("program.prefix");
                 }),
                 (Action<object>)(instance =>
                 {
                     if (!ReferenceEquals(instance, device) || device.Disposed)
                         throw new Exception("Device-disposal notification ran too late.");
                     order.Add("device.prefix");
                 })]);
            program.Dispose();
            if (!order.SequenceEqual(new[] { "program.prefix", "program.body", "device.prefix", "device.body" }))
                throw new Exception("Graphics lifetime hooks did not precede native teardown.");
        }
        finally { patch.GetMethod("Uninstall")!.Invoke(null, null); }
        order.Clear();
        program.Dispose();
        if (!order.SequenceEqual(new[] { "program.body", "device.body" }))
            throw new Exception("Graphics lifetime hooks remained installed after unload.");
        Console.WriteLine("Graphics cleanup ordering and hook removal checks passed.");
    }

    private sealed class DeviceLifetimeProbe(List<string> order)
    {
        internal bool Disposed { get; private set; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Dispose()
        {
            order.Add("device.body");
            Disposed = true;
        }
    }

    private sealed class ProgramLifetimeProbe(List<string> order, DeviceLifetimeProbe device)
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Dispose()
        {
            order.Add("program.body");
            device.Dispose();
        }
    }

    private static void InspectInstalledHook(Type patch, string gameDir)
    {
        Assembly? ResolveGameAssembly(AssemblyLoadContext context, AssemblyName name)
        {
            var path = Path.Combine(gameDir, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(Path.GetFullPath(path)) : null;
        }
        AssemblyLoadContext.Default.Resolving += ResolveGameAssembly;
        try
        {
            // Verify compatibility without executing KSA's GPU code or copying game assemblies.
            var gameAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(gameDir, "KSA.dll")));
            var program = gameAssembly.GetType("KSA.Program", true)!;
            var graphicsPatch = patch.Assembly.GetType("KSI2.Optional.HarmonyGraphicsLifetimePatch", true)!;
            var programDispose = program.GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)!;
            var deviceDispose = program.GetMethod("GetRenderer", BindingFlags.Public | BindingFlags.Static)!
                .ReturnType.GetProperty("Device")!.PropertyType.GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)!;
            try
            {
                graphicsPatch.GetMethod("Install")!.Invoke(null,
                    [programDispose, deviceDispose, (Action<object>)(_ => { }), (Action<object>)(_ => { })]);
            }
            finally { graphicsPatch.GetMethod("Uninstall")!.Invoke(null, null); }
            var renderer = gameAssembly.GetType("KSA.Rendering.PostProcessing.Cmaa2Renderer", true)!;
            var gameTarget = renderer.GetMethod("RenderPreparePasses", BindingFlags.Instance | BindingFlags.Public);
            if (gameTarget is null || program.GetField("_cmaa2Renderers", BindingFlags.Instance | BindingFlags.NonPublic) is null
                || renderer.GetField("_sceneLdrTarget", BindingFlags.Instance | BindingFlags.NonPublic) is null)
                throw new Exception("Installed KSA does not expose the inspected private render members.");
            try
            {
                patch.GetMethod("Install")!.Invoke(null,
                    [gameTarget, (Action<object, object, int>)((_, _, _) => { })]);
            }
            finally { patch.GetMethod("Uninstall")!.Invoke(null, null); }
            Console.WriteLine("Installed KSA render method accepts the Harmony postfix; GPU execution remains untested.");
        }
        finally { AssemblyLoadContext.Default.Resolving -= ResolveGameAssembly; }
    }

    private sealed class Probe
    {
        public void Run(CommandProbe command, int frame) => Prepare(command, frame);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Prepare(CommandProbe command, int frame) { }
    }

    private readonly record struct CommandProbe(int Value);
}
