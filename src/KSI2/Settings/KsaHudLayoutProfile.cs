using System.Reflection;
using KSA;

namespace KSI2;

internal static class KsaHudLayoutProfile
{
    private const string ProfileName = "KSI2 Default";
    private const int ProfileRevision = 10;
    private const float RightMarginUv = 0.017778f;
    private const float PortraitTopOffsetUv = -0.04f;
    private const float PortraitTopOffsetPixels = 75f;
    private static readonly MethodInfo? MarkEditorWindowDirty = typeof(GaugeCanvas).GetMethod(
        "MarkEditorWindowDirty", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? LastAutoSizedTileCount = typeof(CrewPortraitPanel).GetField(
        "_lastAutoSizedTileCount", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo? ResizeCanvasToOccupantCount = typeof(CrewPortraitPanel).GetMethod(
        "ResizeCanvasToOccupantCount", BindingFlags.Static | BindingFlags.NonPublic);
    private static LayoutSave? _lastSelectedLayout;
    private static int _lastWindowWidth;
    private static int _lastWindowHeight;
    private static float _lastPortraitWidth;
    private static float _lastPortraitHeight;
    private static bool _portraitAutoSizeFailed;
    private static string ProfileRevisionPath => Path.Combine(PilotUiSettings.ModDirectory,
        "ksi2.hud-layout-profile-revision");

    internal static void EnsureInstalled()
    {
        var profileDirectory = Path.Combine(LayoutSaves.SaveFolderPath, ProfileName);
        if (Directory.Exists(profileDirectory))
        {
            // Migrate the profile once so existing installs get the portrait
            // placement while later user edits remain untouched.
            var existing = LayoutSave.FromDirectory(profileDirectory);
            if (existing is not null && GetProfileRevision() < ProfileRevision
                && TryUpdateProfile(existing))
                WriteProfileRevision();
            return;
        }

        var navball = GaugeCanvas.AllCanvases.FirstOrDefault(canvas => canvas.Id == "Navball");
        if (navball is null)
        {
            Console.Error.WriteLine("KSI2: could not create the HUD layout because KSA's Navball canvas was not found.");
            return;
        }

        var layout = SerializedLayout.CaptureCurrent();
        var navballLayout = layout.Canvases.FirstOrDefault(canvas => canvas.Id == navball.Id);
        var telemetry = layout.Canvases.FirstOrDefault(canvas => canvas.Id == "Telemetry");
        if (navballLayout is null || telemetry is null)
        {
            Console.Error.WriteLine("KSI2: could not create the HUD layout because KSA did not export its Navball or Telemetry canvas.");
            return;
        }
        navballLayout.Enabled = false;
        telemetry.Enabled = false;
        if (!PositionPortraitAtRightMargin(layout))
        {
            Console.Error.WriteLine("KSI2: could not create the HUD layout because KSA's portrait canvas was not found.");
            return;
        }

        var parentDirectory = LayoutSaves.SaveFolderPath;
        var stagingDirectory = Path.Combine(parentDirectory, $".{ProfileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(parentDirectory);
            Directory.CreateDirectory(stagingDirectory);
            layout.WriteTo(new DirectoryInfo(stagingDirectory));

            var metadata = new HudLayoutMetaData(ProfileName, stagingDirectory)
            {
                IsDefault = false,
                HudKey = InputAction.None.ToString()
            };
            metadata.Write();

            // Do not overwrite a layout the user created while KSI2 was starting.
            if (Directory.Exists(profileDirectory))
            {
                LayoutSave.FromDirectory(profileDirectory);
                return;
            }

            Directory.Move(stagingDirectory, profileDirectory);
            if (LayoutSave.FromDirectory(profileDirectory) is null)
                Console.Error.WriteLine("KSI2: KSA did not register the generated 'KSI2 Default' HUD layout.");
            else
            {
                WriteProfileRevision();
                Console.WriteLine("KSI2: added the 'KSI2 Default' KSA HUD layout (navball and telemetry hidden, portrait at the right margin).");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"KSI2: could not create the KSA HUD layout: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                try { Directory.Delete(stagingDirectory, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"KSI2: could not remove temporary HUD layout files: {ex.Message}");
                }
            }
        }
    }

    internal static void UpdatePortraitPosition()
    {
        var selected = LayoutSaves.Selected;
        if (selected?.Id != ProfileName)
        {
            _lastSelectedLayout = selected;
            return;
        }

        var portrait = GaugeCanvas.AllCanvases.FirstOrDefault(canvas => canvas.Id == "CrewPortraits");
        if (portrait is null) return;

        var windowSize = KSA.Program.GetWindow().Size;
        if (windowSize.X <= 0 || windowSize.Y <= 0) return;

        var portraitSize = portrait.GetPixelsSize();
        var changed = selected != _lastSelectedLayout || windowSize.X != _lastWindowWidth
            || windowSize.Y != _lastWindowHeight || portraitSize.X != _lastPortraitWidth
            || portraitSize.Y != _lastPortraitHeight;

        if (changed && CrewPortraitPanel.HasOccupants && !_portraitAutoSizeFailed
            && LastAutoSizedTileCount is not null && ResizeCanvasToOccupantCount is not null)
        {
            // KSA only recalculates this size when the crew count changes.
            // Applying a HUD layout resets it even when the crew is unchanged.
            try
            {
                LastAutoSizedTileCount.SetValue(null, -1);
                ResizeCanvasToOccupantCount.Invoke(null, null);
                portrait.ClearDirty();
            }
            catch (Exception ex) when (ex is TargetInvocationException or FieldAccessException
                or MethodAccessException or ArgumentException)
            {
                _portraitAutoSizeFailed = true;
                Console.Error.WriteLine($"KSI2: could not refresh KSA's crew portrait size: {ex.Message}");
            }
            portraitSize = portrait.GetPixelsSize();
        }

        _lastSelectedLayout = selected;
        _lastWindowWidth = windowSize.X;
        _lastWindowHeight = windowSize.Y;
        _lastPortraitWidth = portraitSize.X;
        _lastPortraitHeight = portraitSize.Y;

        // KSA keeps an edited canvas dirty until the profile is reapplied. Let
        // the user move it freely, but still respond to resolution changes.
        if (!changed && LayoutSaves.IsCurrentModified()) return;

        var portraitMin = portrait.GetPixelsMin();
        var targetRight = windowSize.X - RightMarginUv * windowSize.Y;
        var horizontalAdjustment = targetRight - (portraitMin.X + portraitSize.X);
        var verticalAdjustment = PortraitTopOffsetUv * windowSize.Y
            + PortraitTopOffsetPixels - portraitMin.Y;
        if (MathF.Abs(horizontalAdjustment) < 0.5f && MathF.Abs(verticalAdjustment) < 0.5f)
            return;

        portrait.Offset.X += horizontalAdjustment / windowSize.Y;
        portrait.Offset.Y += verticalAdjustment / windowSize.Y;
        portrait.Offset.OnDataLoad(KSA.Mod.Empty);
        portrait.CacheTransform();
        portrait.Update(portrait.GetPixelsMin(), portrait.GetPixelsSize(), resetWindow: true);
        MarkEditorWindowDirty?.Invoke(portrait, null);
    }

    private static bool TryUpdateProfile(LayoutSave save)
    {
        var navball = save.LayoutData.Canvases.FirstOrDefault(canvas => canvas.Id == "Navball");
        var portrait = save.LayoutData.Canvases.FirstOrDefault(canvas => canvas.Id == "CrewPortraits");
        var telemetry = save.LayoutData.Canvases.FirstOrDefault(canvas => canvas.Id == "Telemetry");
        if (navball is null || portrait is null || telemetry is null
            || !PositionPortraitAtRightMargin(save.LayoutData)) return false;

        navball.Enabled = false;
        telemetry.Enabled = false;
        var stagingDirectory = Path.Combine(save.Directory.Parent!.FullName,
            $".{ProfileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            save.LayoutData.WriteTo(new DirectoryInfo(stagingDirectory));
            File.Move(Path.Combine(stagingDirectory, "layout.xml"),
                Path.Combine(save.Directory.FullName, "layout.xml"), overwrite: true);
            LayoutSave.FromDirectory(save.Directory.FullName);
            Console.WriteLine("KSI2: updated the 'KSI2 Default' KSA HUD layout (navball and telemetry hidden, portrait at the right margin).");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"KSI2: could not update the KSA HUD layout: {ex.Message}");
            return false;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                try { Directory.Delete(stagingDirectory, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"KSI2: could not remove temporary HUD layout files: {ex.Message}");
                }
            }
        }
    }

    private static bool PositionPortraitAtRightMargin(SerializedLayout layout)
    {
        var portraitCanvas = GaugeCanvas.AllCanvases.FirstOrDefault(item => item.Id == "CrewPortraits");
        var portrait = layout.Canvases.FirstOrDefault(item => item.Id == "CrewPortraits");
        var windowSize = KSA.Program.GetWindow().Size;
        if (portraitCanvas is null || portrait is null || windowSize.Y <= 0) return false;

        // Match KSA's original Telemetry right margin without following later
        // movements of that canvas.
        var aspect = (float)windowSize.X / windowSize.Y;
        var sizeX = (float)portrait.Size.X;
        var sizeY = (float)portrait.Size.Y;
        var portraitAnchor = portraitCanvas.Anchor.ToUv();
        var portraitPivot = portraitCanvas.Pivot.ToUv();
        var portraitLeft = aspect - RightMarginUv - sizeX;
        var topOffset = PortraitTopOffsetUv + PortraitTopOffsetPixels / windowSize.Y;
        portrait.Offset.X = portraitLeft - portraitAnchor.X * aspect + portraitPivot.X * sizeX;
        portrait.Offset.Y = topOffset - portraitAnchor.Y + portraitPivot.Y * sizeY;
        return true;
    }

    private static int GetProfileRevision()
    {
        try
        {
            return int.TryParse(File.ReadAllText(ProfileRevisionPath), out var revision) ? revision : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void WriteProfileRevision()
    {
        var temporaryPath = ProfileRevisionPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, ProfileRevision.ToString());
            File.Move(temporaryPath, ProfileRevisionPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"KSI2: could not save HUD layout profile revision: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"KSI2: could not remove temporary layout revision file: {ex.Message}");
            }
        }
    }
}
