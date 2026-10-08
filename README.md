# KSI2

KSI2 is a KSP2-inspired interface mod for Kitten Space Agency, loaded through StarMap.

The project is in early development.

## Requirements

- Kitten Space Agency
- StarMap
- .NET 10 SDK

## Build

Local builds use KSA reference assemblies from an `Import` directory next to this repository. Copy `KSA.dll` and the `Brutal.*.dll` assemblies from the matching KSA installation into that directory. These proprietary assemblies must not be committed or redistributed.

You can use a different directory by passing `-p:KsaReferencePath=C:\path\to\assemblies`.

```shell
dotnet restore KSI2.sln
dotnet build KSI2.sln --configuration Release --no-restore
```

Build output is written to `src/KSI2/bin/Release/net10.0`.

## Throttle artwork

The first overlay is a display only throttle gauge. Its source artwork is kept out of Git because it comes from a local AssetRipper export. Import it from an export whose root contains `Texture2D`, `Sprite`, and `group_throttle.prefab`:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Import-ThrottleAssets.ps1 `
  -ExportRoot 'C:\path\to\ExportedProject\Assets'
```

The importer copies eight required sprite textures, generates a filtered green fill texture, extracts the original `%` glyph from its KSP2 font atlas, validates their dimensions, and updates the checked in [sprite and prefab reference](docs/throttle-assets.reference.json). A Release build copies the artwork to `src/KSI2/bin/Release/net10.0/[asset package]` beside the mod DLL. Copy the complete `assets` directory with the DLL, deps file, manifest XML, and `mod.toml` when installing. The mod never reads the original temporary export directory.

If the artwork is absent, the gauge stays hidden and KSI2 reports the missing files in its settings window. The KSI2 window provides visibility, layout editing, scale, and reset controls; settings are stored beside the installed DLL in `ksi2.settings.json`.

The renderer uses KSA's registered texture path for the artwork and retains CPU geometry as a fallback. See the [rendering and coordinate audit](docs/throttle-rendering.md) for the source evidence and the [UI texture rendering notes](docs/ui-texture-rendering.md) before implementing other small interface art. In **Edit layout**, the optional **Preview throttle** slider changes only the displayed value. Turn off edit mode to return to live telemetry. The settings window also shows the live manual throttle for comparison.

The handle number can use a locally generated atlas from a font you provide:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Generate-HandleFontAtlas.ps1 `
  -OtfPath 'C:\path\to\your\font.otf'
```

This creates `external asset package/throttle/[asset]`, which the Release build copies with the other artwork. The OTF file stays local; the generated PNG is tracked. If the atlas is absent from an installation, the handle number uses KSA's active ImGui font.

KSA's texture registration path is used for direct image rendering. Pixel equivalence to KSP2 remains subject to in-game comparison.

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
- `[asset] (the complete folder)

Enable `KSI2` in KSA's user `manifest.toml`, then launch the game through StarMap.

## License

KSI2 is available under the MIT License. It is an independent community project and is not affiliated with or endorsed by the developers or publishers of Kerbal Space Program 2 or Kitten Space Agency.
