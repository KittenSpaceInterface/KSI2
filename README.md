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

## Install

Create `KSI2` under KSA's user mod directory:

```text
Documents/My Games/Kitten Space Agency/mods/KSI2/
```

Copy these files from the Release output:

- `KSI2.dll`
- `KSI2.deps.json`
- `mod.toml`

Enable `KSI2` in KSA's user `manifest.toml`, then launch the game through StarMap.

## License

KSI2 is available under the MIT License. It is an independent community project and is not affiliated with or endorsed by the developers or publishers of Kerbal Space Program 2 or Kitten Space Agency.
