using Brutal.GlfwApi;

namespace KSI2;

internal static class PilotScaleViewport
{
    internal static Point Resolve(float width, float height)
    {
        var actual = new Point(width, height);
        if (KSA.Program.GetWindow().IsFullscreen) return actual;

        var settings = KSA.GameSettings.Current;
        int expectedWidth;
        int expectedHeight;
        int intendedWidth;
        int intendedHeight;

        if (settings.ScreenDisplayMode == KSA.GameSettings.DisplayMode.Borderless)
        {
            var monitor = KSA.GameSettings.GetMonitor(settings.ScreenIndex, settings.ScreenName);
            var videoMode = monitor.VideoMode;
            if (settings.AlwaysOnTop) return actual;
            monitor.GetMonitorWorkArea(out _, out var workArea);
            expectedWidth = workArea.X;
            expectedHeight = workArea.Y;
            intendedWidth = videoMode.Width;
            intendedHeight = videoMode.Height;
        }
        else
        {
            var monitor = Glfw.PrimaryMonitor;
            monitor.GetMonitorWorkArea(out _, out var workArea);
            var videoMode = monitor.VideoMode;
            intendedWidth = Math.Min(settings.ScreenWidth, videoMode.Width);
            intendedHeight = Math.Min(settings.ScreenHeight, videoMode.Height);
            expectedWidth = Math.Min(workArea.X - settings.ScreenPosX, settings.ScreenWidth);
            expectedHeight = Math.Min(workArea.Y - settings.ScreenPosY, settings.ScreenHeight);
        }

        if (expectedWidth <= 0 || expectedHeight <= 0 ||
            intendedWidth <= 0 || intendedHeight <= 0) return actual;

        // KSA caps windowed and borderless windows to the monitor work area.
        // Restore the size that produced the selected resolution's UI scale,
        // while retaining any additional user resize of the actual window.
        return new Point(
            MathF.Min(width * intendedWidth / expectedWidth, intendedWidth),
            MathF.Min(height * intendedHeight / expectedHeight, intendedHeight));
    }
}
