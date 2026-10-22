using System.Reflection;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA.Rendering;
using KSA.Rendering.PostProcessing;

namespace KSI2;

// All assumptions about private KSA rendering members live here.
internal sealed class PanelBlurRenderBridge : IDisposable
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly FieldInfo _renderers;
    private readonly FieldInfo _ldrTarget;
    private Type? _patchType;
    private readonly Action<CommandBuffer, int> _prepare;
    internal string? Error { get; private set; }
    internal int HookCalls { get; private set; }
    internal int MainHookCalls { get; private set; }

    internal PanelBlurRenderBridge(Action<CommandBuffer, int> prepare)
    {
        _prepare = prepare;
        _renderers = typeof(KSA.Program).GetField("_cmaa2Renderers", PrivateInstance)
            ?? throw new MissingFieldException("KSA.Program._cmaa2Renderers");
        _ldrTarget = typeof(Cmaa2Renderer).GetField("_sceneLdrTarget", PrivateInstance)
            ?? throw new MissingFieldException("Cmaa2Renderer._sceneLdrTarget");
        // The Program wrapper is small enough to be inlined before Harmony installs a patch.
        // This larger method ends after CMAA2 transitions the LDR image to SampledReadF.
        var target = typeof(Cmaa2Renderer).GetMethod(nameof(Cmaa2Renderer.RenderPreparePasses),
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException("Cmaa2Renderer.RenderPreparePasses");
        var parameters = target.GetParameters();
        if (parameters.Length != 3 || parameters[0].ParameterType != typeof(CommandBuffer)
            || parameters[1].ParameterType != typeof(int) || !parameters[2].ParameterType.IsByRef)
            throw new MissingMethodException("Cmaa2Renderer.RenderPreparePasses signature changed");
        var dir = Path.Combine(PilotUiSettings.ModDirectory, "assets", "optional");
        OptionalAssemblyLoader.Load(Path.Combine(dir, "0Harmony.dll"));
        _patchType = OptionalAssemblyLoader.Load(Path.Combine(dir, "KSI2.FlightComputerInputPatch.dll"))
            .GetType("KSI2.Optional.HarmonyPanelBlurPatch", true)!;
        try
        {
            _patchType.GetMethod("Install")!.Invoke(null,
                [target, (Action<object, object, int>)OnPrepared]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal RenderTarget? GetSceneTarget()
    {
        if (_renderers.GetValue(KSA.Program.Instance) is not Cmaa2Renderer[] renderers) return null;
        var slot = KSA.Program.MainViewport.ShaderSlot;
        if (slot < 0 || slot >= renderers.Length || !renderers[slot].Enabled) return null;
        return _ldrTarget.GetValue(renderers[slot]) as RenderTarget;
    }

    private void OnPrepared(object renderer, object command, int frame)
    {
        HookCalls++;
        try
        {
            // A main view only implementation: never replace the output with a secondary view.
            if (_renderers.GetValue(KSA.Program.Instance) is not Cmaa2Renderer[] renderers) return;
            var slot = KSA.Program.MainViewport.ShaderSlot;
            if (slot < 0 || slot >= renderers.Length || !ReferenceEquals(renderer, renderers[slot])) return;
            MainHookCalls++;
            _prepare((CommandBuffer)command, frame);
        }
        catch (Exception ex)
        {
            Error = ex.GetBaseException().Message;
            Console.Error.WriteLine($"KSI2: background render hook failed: {Error}");
        }
    }

    public void Dispose()
    {
        _patchType?.GetMethod("Uninstall")?.Invoke(null, null);
        _patchType = null;
    }
}
