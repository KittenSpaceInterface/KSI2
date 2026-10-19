using System.Reflection;
using Brutal.Numerics;
using KSA;

namespace KSI2;

internal static class KsaHudLayoutProfile
{
    private const string ProfileName = "KSI2 Default";
    private const int ProfileRevision = 14;
    private const float EngineRightPixels = 13f;
    private const float EngineUpPixels = 55f;
    private const float RightMarginUv = 0.017778f;
    private const float PortraitTopOffsetUv = -0.04f;
    private const float PortraitTopOffsetPixels = 75f;
    private static readonly MethodInfo? MarkEditorWindowDirty = typeof(GaugeCanvas).GetMethod(
        "MarkEditorWindowDirty", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CanvasDefaults = typeof(GaugeCanvas).GetField(
        "_defaults", BindingFlags.Instance | BindingFlags.NonPublic);
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
    private static LayoutSave? _lastEngineLayout;
    private static object? _lastEngineOffsetReference;
    private static int _lastEngineWindowHeight;
    private static double _lastEngineAppliedX;
    private static double _lastEngineAppliedY;
    private static bool _engineUsesDefaultOffset;
    private static string ProfileRevisionPath => Path.Combine(PilotUiSettings.ModDirectory,
        "ksi2.hud-layout-profile-revision");

    internal static void EnsureInstalled()
    {
        var profileDirectory = Path.Combine(LayoutSaves.SaveFolderPath, ProfileName);
        if (Directory.Exists(profileDirectory))
        {
            // Migrate once so existing installs get the KSI2 HUD defaults;
            // later user edits remain untouched.
            var existing = LayoutSave.FromDirectory(profileDirectory);
            var previousRevision = GetProfileRevision();
            if (existing is not null && previousRevision < ProfileRevision
                && TryUpdateProfile(existing, previousRevision))
            {
                if (LayoutSave.FromDirectory(profileDirectory) is { } migrated)
                {
                    LayoutSaves.SetDefault(migrated);
                    migrated.Apply();
                }
                WriteProfileRevision();
            }
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
        if (!PositionEngineControl(layout))
        {
            Console.Error.WriteLine("KSI2: could not position KSA's engine control in the HUD layout.");
            return;
        }
        if (!ConfigureAttitudeLayout(layout))
        {
            Console.Error.WriteLine("KSI2: could not create the HUD layout because its attitude canvases were not found.");
            return;
        }
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
                IsDefault = true,
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
            var registered = LayoutSave.FromDirectory(profileDirectory);
            if (registered is null)
                Console.Error.WriteLine("KSI2: KSA did not register the generated 'KSI2 Default' HUD layout.");
            else
            {
                LayoutSaves.SetDefault(registered);
                registered.Apply();
                WriteProfileRevision();
                Console.WriteLine("KSI2: added the 'KSI2 Default' KSA HUD layout (engine control offset from its built-in position, navball and telemetry hidden, portrait at the right margin).");
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

    internal static void UpdateEnginePosition()
    {
        var selected = LayoutSaves.Selected;
        if (selected?.Id != ProfileName)
        {
            _lastEngineLayout = selected;
            _lastEngineOffsetReference = null;
            return;
        }

        var engine = GaugeCanvas.AllCanvases.FirstOrDefault(canvas => canvas.Id == "EngineControl");
        var windowHeight = KSA.Program.GetWindow().Size.Y;
        if (engine is null || windowHeight <= 0 || !TryGetEngineDefaults(engine, out var offset, out _))
            return;

        if (!ReferenceEquals(selected, _lastEngineLayout)
            || !ReferenceEquals(engine.Offset, _lastEngineOffsetReference))
        {
            _lastEngineLayout = selected;
            _lastEngineOffsetReference = engine.Offset;
            _lastEngineWindowHeight = 0;
            var deltaX = engine.Offset.X - offset.X;
            var deltaY = engine.Offset.Y - offset.Y;
            _engineUsesDefaultOffset = deltaX > 0.00001
                && Math.Abs(deltaY + EngineUpPixels * deltaX / EngineRightPixels) < 0.0001;
            _lastEngineAppliedX = engine.Offset.X;
            _lastEngineAppliedY = engine.Offset.Y;
        }
        if (!_engineUsesDefaultOffset || _lastEngineWindowHeight == windowHeight) return;
        // Stop automatic positioning after the user moves the canvas in KSA's editor.
        if (_lastEngineWindowHeight != 0
            && (Math.Abs(engine.Offset.X - _lastEngineAppliedX) > 0.00001
                || Math.Abs(engine.Offset.Y - _lastEngineAppliedY) > 0.00001))
        {
            _engineUsesDefaultOffset = false;
            return;
        }

        var targetX = offset.X + EngineRightPixels / windowHeight;
        var targetY = offset.Y - EngineUpPixels / windowHeight;
        _lastEngineWindowHeight = windowHeight;
        _lastEngineAppliedX = targetX;
        _lastEngineAppliedY = targetY;
        if (Math.Abs(engine.Offset.X - targetX) < 0.00001
            && Math.Abs(engine.Offset.Y - targetY) < 0.00001) return;

        engine.Offset.X = targetX;
        engine.Offset.Y = targetY;
        engine.Offset.OnDataLoad(KSA.Mod.Empty);
        engine.CacheTransform();
        engine.Update(engine.GetPixelsMin(), engine.GetPixelsSize(), resetWindow: true);
        MarkEditorWindowDirty?.Invoke(engine, null);
    }

    private static bool TryUpdateProfile(LayoutSave save, int previousRevision)
    {
        var navball = save.LayoutData.Canvases.FirstOrDefault(canvas => canvas.Id == "Navball");
        var portrait = save.LayoutData.Canvases.FirstOrDefault(canvas => canvas.Id == "CrewPortraits");
        var telemetry = save.LayoutData.Canvases.FirstOrDefault(canvas => canvas.Id == "Telemetry");
        if (navball is null || portrait is null || telemetry is null
            || !PositionPortraitAtRightMargin(save.LayoutData)) return false;

        navball.Enabled = false;
        telemetry.Enabled = false;
        if (previousRevision < 13 && !ConfigureAttitudeLayout(save.LayoutData)) return false;
        if (!PositionEngineControl(save.LayoutData)) return false;
        var stagingDirectory = Path.Combine(save.Directory.Parent!.FullName,
            $".{ProfileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            save.LayoutData.WriteTo(new DirectoryInfo(stagingDirectory));
            File.Move(Path.Combine(stagingDirectory, "layout.xml"),
                Path.Combine(save.Directory.FullName, "layout.xml"), overwrite: true);
            LayoutSave.FromDirectory(save.Directory.FullName);
            Console.WriteLine("KSI2: updated the 'KSI2 Default' KSA HUD layout (engine control offset from its built-in position, navball and telemetry hidden, portrait at the right margin).");
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

    private static bool ConfigureAttitudeLayout(SerializedLayout layout)
    {
        var original = layout.Canvases.FirstOrDefault(canvas => canvas.Id == "PitchYawRoll");
        var sequence = layout.Canvases.FirstOrDefault(canvas => canvas.Id == "Sequence");
        var replacement = layout.Canvases.FirstOrDefault(canvas => canvas.Id == "KSI2AttitudeIndicators");
        if (original is null || sequence is null) return false;

        if (replacement is null)
        {
            var replacementCanvas = GaugeCanvas.AllCanvases.FirstOrDefault(
                canvas => canvas.Id == "KSI2AttitudeIndicators");
            if (replacementCanvas is null) return false;
            replacement = replacementCanvas.ExportLayout();
            replacement.Offset.X = original.Offset.X;
            replacement.Offset.Y = original.Offset.Y;
            replacement.Size.X = original.Size.X;
            replacement.Size.Y = original.Size.Y * 0.5;
            layout.Canvases.Add(replacement);
        }

        original.Enabled = false;
        replacement.Enabled = true;
        sequence.Offset.Y -= original.Size.Y * 0.5;
        return true;
    }

    private static bool PositionEngineControl(SerializedLayout layout)
    {
        var engine = GaugeCanvas.AllCanvases.FirstOrDefault(canvas => canvas.Id == "EngineControl");
        var windowHeight = KSA.Program.GetWindow().Size.Y;
        if (engine is null || windowHeight <= 0 || !TryGetEngineDefaults(engine, out var offset, out var size))
            return false;

        var saved = layout.Canvases.FirstOrDefault(canvas => canvas.Id == "EngineControl");
        if (saved is null)
        {
            saved = engine.ExportLayout();
            layout.Canvases.Add(saved);
        }
        saved.Offset.X = offset.X + EngineRightPixels / windowHeight;
        saved.Offset.Y = offset.Y - EngineUpPixels / windowHeight;
        saved.Size.X = size.X;
        saved.Size.Y = size.Y;
        saved.Enabled = true;
        return true;
    }

    private static bool TryGetEngineDefaults(GaugeCanvas engine, out float2 offset, out float2 size)
    {
        offset = default;
        size = default;
        var defaults = CanvasDefaults?.GetValue(engine);
        if (defaults is null) return false;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = defaults.GetType();
        if (type.GetField("OffsetUv", flags)?.GetValue(defaults) is not float2 authoredOffset
            || type.GetField("SizeUv", flags)?.GetValue(defaults) is not float2 authoredSize)
            return false;
        offset = authoredOffset;
        size = authoredSize;
        return true;
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
