using Brutal.ImGuiApi;
using KSA;

namespace KSI2;

internal static class KsaHudLayoutProfile
{
    private const string ProfileName = "KSI2 Default";
    private const int ProfileRevision = 6;
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
        if (navballLayout is null)
        {
            Console.Error.WriteLine("KSI2: could not create the HUD layout because KSA did not export its Navball canvas.");
            return;
        }
        navballLayout.Enabled = false;
        if (!AlignPortraitWithPeriapsis(layout))
        {
            Console.Error.WriteLine("KSI2: could not create the HUD layout because KSA's portrait or periapsis canvas was not found.");
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
                Console.WriteLine("KSI2: added the 'KSI2 Default' KSA HUD layout (navball hidden, portrait aligned with periapsis).");
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

    private static bool TryUpdateProfile(LayoutSave save)
    {
        var navball = save.LayoutData.Canvases.FirstOrDefault(canvas => canvas.Id == "Navball");
        var portrait = save.LayoutData.Canvases.FirstOrDefault(canvas => canvas.Id == "CrewPortraits");
        if (navball is null || portrait is null || !AlignPortraitWithPeriapsis(save.LayoutData)) return false;

        navball.Enabled = false;
        var stagingDirectory = Path.Combine(save.Directory.Parent!.FullName,
            $".{ProfileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            save.LayoutData.WriteTo(new DirectoryInfo(stagingDirectory));
            File.Move(Path.Combine(stagingDirectory, "layout.xml"),
                Path.Combine(save.Directory.FullName, "layout.xml"), overwrite: true);
            LayoutSave.FromDirectory(save.Directory.FullName);
            Console.WriteLine("KSI2: updated the 'KSI2 Default' KSA HUD layout (navball hidden, portrait aligned with periapsis).");
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

    private static bool AlignPortraitWithPeriapsis(SerializedLayout layout)
    {
        var portraitCanvas = GaugeCanvas.AllCanvases.FirstOrDefault(item => item.Id == "CrewPortraits");
        var periapsisCanvas = GaugeCanvas.AllCanvases.FirstOrDefault(item => item.Id == "Telemetry");
        var portrait = layout.Canvases.FirstOrDefault(item => item.Id == "CrewPortraits");
        var periapsis = layout.Canvases.FirstOrDefault(item => item.Id == "Telemetry");
        var viewport = ImGui.GetMainViewport();
        if (portraitCanvas is null || periapsisCanvas is null || portrait is null || periapsis is null
            || viewport.Size.Y <= 0) return false;

        // Periapsis is part of KSA's Telemetry canvas. Align the portrait's
        // right edge with that canvas and shift it above the viewport edge to
        // account for the portrait's internal top spacing.
        var aspect = viewport.Size.X / viewport.Size.Y;
        var sizeX = (float)portrait.Size.X;
        var sizeY = (float)portrait.Size.Y;
        var portraitAnchor = portraitCanvas.Anchor.ToUv();
        var portraitPivot = portraitCanvas.Pivot.ToUv();
        var periapsisAnchor = periapsisCanvas.Anchor.ToUv();
        var periapsisPivot = periapsisCanvas.Pivot.ToUv();
        var periapsisRight = periapsisAnchor.X * aspect + (float)periapsis.Offset.X
            + (1f - periapsisPivot.X) * (float)periapsis.Size.X;
        var portraitLeft = periapsisRight - sizeX;
        var topOffset = -0.04f + 6f / viewport.Size.Y;
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
