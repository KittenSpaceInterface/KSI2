# KSI2

KSI2 is a KSP2-inspired interface mod for Kitten Space Agency, loaded through StarMap.

The project is in early development.

The current viewport and navball placement calculations are documented in
[docs/screen-layout.md](docs/screen-layout.md).
Deferred experimental controls and panel blur are recorded in
[docs/future-ui-plan.md](docs/future-ui-plan.md).

## Requirements

- Kitten Space Agency
- StarMap
- .NET 10 SDK

## Build

`Directory.Build.props` defaults `KsaReferencePath` to an `Import` directory one level above this repository. In this workspace that resolves to `D:\ksi-project\Import`; if it is absent, the build reports that KSA reference assemblies were not found. Point `KsaReferencePath` at the local KSA installation directory containing `KSA.dll` and the `Brutal.*.dll` assemblies; avoid copying these proprietary game assemblies into the repository.

```shell
dotnet restore KSI2.sln
dotnet build KSI2.sln --configuration Release --no-restore
```

When the assemblies are elsewhere, pass their directory as an MSBuild property:

```powershell
dotnet restore KSI2.sln -p:KsaReferencePath="C:\path\to\Kitten Space Agency"
dotnet build KSI2.sln --configuration Release --no-restore -p:KsaReferencePath="C:\path\to\Kitten Space Agency"
```

Build output is written to `src/KSI2/bin/Release/net10.0`.

## Throttle gauge

The throttle gauge follows the controlled vehicle's manual throttle. Drag its
handle to change throttle through KSA's input actions at KSA's normal rate.
The **Experimental** setting **Mouse throttle control** enables direct control;
it is off by default. The settings window lets you adjust the interface scale
and position.
**Edit layout** lets you drag the interface as a group. Settings are saved
beside the mod.

The default instrument positions and draw order are in
`ksi2.layout.defaults.json`. To keep personal adjustments across installs,
place a sparse `ksi2.layout.json` beside the mod DLL; see
[`docs/pilot-layout-json.md`](docs/pilot-layout-json.md). KSI2 reads these files
when it starts. The in-game combined drag still saves its position in
`ksi2.settings.json`.

The **Experimental** setting **Blur panel backgrounds** covers the four flight
panel backings. Its switch and opacity are saved in `ksi2.settings.json`. After
enabling blur, KSI2 checks for five seconds that KSA stays open; if KSA exits
during that period, blur is turned off on the next launch. The same check runs
when KSI2 starts with blur enabled. A KSA version change also turns off both
experimental options so they can be rechecked against the new version.

The **Experimental** setting **Add KSI2 to HUD menu** adds a checked **KSI2
Configuration** item below Navball in KSA's HUD menu. The item shows or hides
the KSI2 settings window. Once the HUD item has appeared, the settings window's
title-bar close button also hides that window; the HUD item can show it again.
The flight instruments remain visible independently. This option is off by default. Its Harmony patch is loaded only
when enabled and is removed when disabled. If KSA exits unexpectedly while it
is enabled during the five-second check, KSI2 disables the option on the next
launch. The same check runs at startup when the option is enabled. A missing patch or a
changed KSA HUD list also leaves the option off and the flight instruments
visible. This option does not change KSA's Navball visibility.

Under **Advanced**, **Vertical Speed Configuration** toggles the calibration
window. The window also has a Close button and starts closed when KSI2 loads.

## Geometry checks

Run the focused handle, fill, telemetry, and settings checks with:

```powershell
dotnet run --project tests/KSI2.GeometryTests --configuration Release
```

To verify the locally imported artwork and generate a five-position preview without launching KSA:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Preview-Throttle.ps1
```

## Install

Create `KSI2` under KSA's user mod directory:

```text
Documents/My Games/Kitten Space Agency/mods/KSI2/
```

Copy these files from the Release output:

- `KSI2.dll`
- `KSI2.deps.json`
- `mod.toml`
- `KSI2Assets.xml`
- `ksi2.layout.defaults.json`
- `assets/` (the complete folder)

Enable `KSI2` in KSA's user `manifest.toml`, then open the game through StarMap. Current StarMap versions look for installed mods and show them when the game opens. If StarMap does not recognize the mod automatically, check that `KSI2` is in the user manifest and installed under the user `mods/` directory.

## License

KSI2 is available under the MIT License. It is an independent community project and is not affiliated with or endorsed by the developers or publishers of Kerbal Space Program 2 or Kitten Space Agency.
