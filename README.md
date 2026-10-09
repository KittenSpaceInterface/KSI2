# KSI2

KSI2 is a KSP2-inspired interface mod for Kitten Space Agency, loaded through StarMap.

The project is in early development.

## Requirements

- Kitten Space Agency
- StarMap
- .NET 10 SDK

## Build

Local builds use KSA reference assemblies from an `Import` directory next to this repository. You can set `KsaReferencePath` to use a different directory.

```shell
dotnet restore KSI2.sln
dotnet build KSI2.sln --configuration Release --no-restore
```

Build output is written to `src/KSI2/bin/Release/net10.0`.

## Throttle gauge

The throttle gauge is a display-only overlay that follows the controlled vehicle's manual throttle by default. Its settings window lets you show or hide it and adjust its scale and position. **Edit layout** offers a temporary throttle preview; turning off edit mode returns to live readings. Settings are saved beside the mod.

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
