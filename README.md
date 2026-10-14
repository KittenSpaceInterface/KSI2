# KSI2

KSI2 is a KSP2-inspired interface mod for Kitten Space Agency, loaded through StarMap.

The project is in early development.

## Releases

`v0.0.1` is the baseline for automated releases. Earlier commits have no
historical release versions or changelog entries. Release Please reads
Conventional Commit messages on `main` since the previous release and uses
their descriptions to draft changelog entries. `feat:` proposes a minor
version, `fix:` proposes a patch version, and a breaking change before `1.0.0`
proposes a minor version. `docs:` and `chore:` commits alone do not propose a
release.

Release Please opens or updates one release pull request when eligible commits
reach `main`; more commits update that same proposal. Merging the release pull
request updates `version.txt` and `CHANGELOG.md` and creates the version tag
and GitHub Release. A mixed batch of features and fixes uses the minor version.
Reaching `1.0.0` requires an intentional release decision after the pilot
interface is complete. GitHub Releases created by this workflow do not include
mod binaries; build and install locally as below.

The workflow uses `GITHUB_TOKEN`. Repository and organization Actions settings
must allow GitHub Actions to create pull requests for the release PR to open.

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

Build output is written to `src/KSI2/bin/Release/net10.0`. It contains KSI2's
code, shaders, optional libraries, and LCD font.

## Throttle gauge

The throttle gauge follows the controlled vehicle's manual throttle. Drag its
handle to change throttle through KSA's input actions at KSA's normal rate.
The **Experimental** setting **Mouse throttle control** enables direct control;
it is off by default. The settings window lets you adjust the interface scale
and position.
**Edit layout** lets you drag the interface as a group. Settings are saved
beside the mod.

**Improved altitude dial** is off by default. When enabled, the right dial
uses 1 km between marks below 1 Mm (for example, `003`, `004`, `005` around
4 km), 1 Mm between marks from 1 Mm, and 1 Gm between marks from 1 Gm.
The scale continues in factors of 1,000 for larger distances, switching before
neighboring dial labels would exceed three digits. The dial
advances gradually between neighboring marks at each scale. When the option
is off, the dial keeps KSP2's 1 km mark spacing below 1 Gm; above that it
widens the spacing to keep the labels within three digits. The switch
is saved in `ksi2.settings.json`. The altitude card uses the same distance unit
order as KSA's apoapsis and periapsis readouts: mm, cm, m, km, Gm, AU, and ly.
It uses their six-cell unit cutoff, changing from km to Gm near 100,000 km.
The altitude card keeps ordinary numbers through 99,999.9 ly and uses compact
scientific notation above that, matching the AP/PE numeric limit.

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

## License

KSI2 is available under the MIT License. It is an independent community project and is not affiliated with or endorsed by the developers or publishers of Kerbal Space Program 2 or Kitten Space Agency.
