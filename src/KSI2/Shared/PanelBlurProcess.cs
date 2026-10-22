using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA;
using KSA.Rendering;
using RenderCore;

namespace KSI2;

internal sealed class PanelBlurProcess : IDisposable
{
    private readonly RendererContext _context;
    private readonly PanelBlurRenderBridge _bridge;
    private RenderTarget? _a;
    private RenderTarget? _b;
    private PanelBlurPass? _downsample;
    private PanelBlurPass? _horizontal;
    private PanelBlurPass? _vertical;
    private VkImageView _source;
    private bool _enabled;
    private bool _requested;
    private bool _failed;
    private bool _available;
    private bool _disposed;
    private int _missedFrames;
    internal bool Ready { get; private set; }
    internal string? Status { get; private set; }
    internal RenderImage? Output => _a?.ColorImage;
    internal RenderImage? SceneColor => _disposed ? null : _bridge.GetSceneTarget()?.ColorImage;

    internal PanelBlurProcess()
    {
        _context = KSA.Program.GetGaugeContext();
        KsaGraphicsLifetime.EnsureInstalled(_context.Renderer.Device);
        if (ModLibrary.Get<ShaderReference>("KSI2BlurPassVert").Shader is null
            || ModLibrary.Get<ShaderReference>("KSI2BlurPassFrag").Shader is null)
            throw new InvalidOperationException("Blur pass shaders were not loaded.");
        _bridge = new PanelBlurRenderBridge(Record);
        KsaGraphicsLifetime.Register(Release);
    }

    internal void BeginFrame(bool enabled)
    {
        if (_disposed || !KsaGraphicsLifetime.CanUse(_context.Renderer.Device)) return;
        if (enabled && _requested && !Ready && !_failed && ++_missedFrames >= 3)
        {
            _failed = true;
            Status = _bridge.Error is not null
                ? "Gaussian (separable) render hook failed. Using solid color."
                : _bridge.HookCalls == 0
                    ? "Gaussian (separable) render hook was not called. Using solid color."
                    : _bridge.MainHookCalls == 0
                        ? "Gaussian (separable) did not find the main view. Using solid color."
                        : "Gaussian (separable) could not prepare the scene. Using solid color.";
            Console.Error.WriteLine($"KSI2: {Status} Hook calls: {_bridge.HookCalls}, "
                + $"main view calls: {_bridge.MainHookCalls}, hook error: {_bridge.Error ?? "none"}.");
        }
        else if (Ready) _missedFrames = 0;
        _enabled = enabled;
        _requested = false;
        _available = false;
        Ready = false;
        if (!enabled || _failed) return;
        try
        {
            var scene = _bridge.GetSceneTarget()?.ColorImage;
            if (scene is null)
            {
                Status = "Gaussian (separable) needs CMAA2 enabled. Using solid color.";
                return;
            }
            if (_context.Renderer.ColorFormat is not (VkFormat.B8G8R8A8UNorm or VkFormat.R8G8B8A8UNorm))
            {
                Status = "Gaussian (separable) is unavailable for this display format. Using solid color.";
                return;
            }
            var extent = new VkExtent2D(Math.Max(1, scene.Width / 2), Math.Max(1, scene.Height / 2));
            if (_a is null || _a.Extent.Width != extent.Width || _a.Extent.Height != extent.Height
                || _source.VkHandle != scene.ImageView.VkHandle)
            {
                // CPU preparation, before KSA records this frame: descriptors never change in flight.
                if (!KsaGraphicsLifetime.TryWaitIdle(_context.Renderer.Device))
                    throw new InvalidOperationException("The graphics device is unavailable.");
                DisposeTargets();
                _source = scene.ImageView;
                _a = new RenderTarget(_context.Renderer, "KSI2 blur A", extent, VkFormat.R8G8B8A8UNorm, VkFormat.Undefined);
                _b = new RenderTarget(_context.Renderer, "KSI2 blur B", extent, VkFormat.R8G8B8A8UNorm, VkFormat.Undefined);
                _downsample = new PanelBlurPass(_context, _a, _source);
                _horizontal = new PanelBlurPass(_context, _b, _a.ColorImage!.ImageView);
                _vertical = new PanelBlurPass(_context, _a, _b.ColorImage!.ImageView);
            }
            Status = null;
            _available = true;
        }
        catch (Exception ex) { Fail(ex); }
    }

    internal bool Request()
    {
        if (!_enabled || !_available || _failed || _a is null || _bridge.GetSceneTarget()?.ColorImage is not { } scene
            || scene.ImageView.VkHandle != _source.VkHandle) return false;
        _requested = true;
        return true;
    }

    private void Record(CommandBuffer command, int frameIndex)
    {
        if (!_enabled || !_requested || _failed || Ready || _disposed
            || !KsaGraphicsLifetime.CanUse(_context.Renderer.Device)) return;
        try
        {
            var scene = _bridge.GetSceneTarget()?.ColorImage;
            if (scene is null || scene.ImageView.VkHandle != _source.VkHandle) return;
            _downsample!.Record(command, float2.Zero);
            for (var i = 0; i < 2; i++)
            {
                _horizontal!.Record(command, new float2(2, 0));
                _vertical!.Record(command, new float2(0, 2));
            }
            Ready = true;
            if (_loggedOutput != Output!.ImageView.VkHandle)
            {
                _loggedOutput = Output.ImageView.VkHandle;
                Console.WriteLine($"KSI2: Gaussian (separable) recorded from {scene.Width}x{scene.Height} LDR: "
                    + $"{Output.Width}x{Output.Height} downsample + H/V/H/V, offset 2, sRGB encode per pass.");
            }
        }
        catch (Exception ex) { Fail(ex); }
        finally { KSA.Program.SetViewport(command); }
    }

    private nint _loggedOutput;

    private void Fail(Exception ex)
    {
        KsaGraphicsLifetime.ObserveFailure(ex);
        Ready = false;
        _failed = true;
        Status = "Gaussian (separable) unavailable. Using solid color.";
        Console.Error.WriteLine($"KSI2: processed panel blur failed: {ex}");
    }

    private void DisposeTargets()
    {
        _downsample?.Dispose(); _downsample = null;
        _horizontal?.Dispose(); _horizontal = null;
        _vertical?.Dispose(); _vertical = null;
        _a?.Dispose(); _a = null;
        _b?.Dispose(); _b = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Release(KsaGraphicsLifetime.TryWaitIdle(_context.Renderer.Device));
    }

    private void Release(bool canUseDevice)
    {
        if (_disposed) return;
        _disposed = true;
        _enabled = false;
        Ready = false;
        KsaGraphicsLifetime.Unregister(Release);
        _bridge.Dispose();
        if (canUseDevice) DisposeTargets();
        else
        {
            // The host will destroy the device; its function table may already be gone.
            _downsample = null;
            _horizontal = null;
            _vertical = null;
            _a = null;
            _b = null;
        }
    }
}
