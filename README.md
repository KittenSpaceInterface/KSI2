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

Local builds use KSA reference assemblies from an `Import` directory next to this repository. To use a different directory, change the default `KsaReferencePath` in [`Directory.Build.props`](Directory.Build.props) at the repository root, or pass it as an MSBuild property, for example `-p:KsaReferencePath="D:\path\to\KSA\assemblies"`.

```shell
dotnet restore KSI2.sln
dotnet build KSI2.sln --configuration Release --no-restore
```

Build output is written to `src/KSI2/bin/Release/net10.0`.

## Throttle gauge

The throttle gauge follows the controlled vehicle's manual throttle. Drag its
handle to change throttle through KSA's input actions at KSA's normal rate.
The **Experimental** setting **Mouse throttle control** enables direct control;
it is off by default. The settings window also lets you show or hide the gauge
and adjust its scale and position.
**Edit layout** offers a display-only throttle preview. Settings are saved
beside the mod.

The **Experimental** setting **Blur panel backgrounds** covers the four flight
panel backings. Its switch and opacity are saved in `ksi2.settings.json`. If
KSA exits unexpectedly while blur is enabled, KSI2 turns it off on the next
launch. To recover manually, set `BlurPanelBackgrounds` to `false` in that
settings file and remove `ksi2.blur-active` from the mod folder.

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
- `assets/` (the complete folder)

Enable `KSI2` in KSA's user `manifest.toml`, then open the game through StarMap. Current StarMap versions look for installed mods and show them when the game opens. If StarMap does not recognize the mod automatically, check that `KSI2` is in the user manifest and installed under the user `mods/` directory.

## License

KSI2 is available under the MIT License. It is an independent community project and is not affiliated with or endorsed by the developers or publishers of Kerbal Space Program 2 or Kitten Space Agency.
