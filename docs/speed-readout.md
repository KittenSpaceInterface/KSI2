# Velocity readout

`SpeedReadoutRenderer` draws the active `GRP-VEL/Container/DataContainer` side card from KSP2's `group_navball.prefab`. Its position follows `NavballClusterLayout`, so the two existing side dials and any future navball placement change move with it. The alternate `DataContainer (1)` above the cluster center is inactive in the saved prefab and is not drawn.

The KSP2 readout uses separate TextMeshPro fields for `VEL`, the mode title, speed digits, and units. This mod renders those fields from locally prepared glyph atlases and TMP metrics. The source SB Liquid font files are absent from the inspected bundles. `Prepare-SpeedReadoutAssets.ps1` converts the exported SDF atlases to ordinary alpha textures for ImGui; its small-letter and digit conversion settings are documented below.

Prepare the local artwork after a Unity Project export of the KSP2 UI bundles:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Prepare-SpeedReadoutAssets.ps1 -ExportedAssets '<AssetRipper-export>\ExportedProject\Assets'
```

The script writes into ignored `external asset package/navball/`. Its source images and generated font data stay local. Build normally afterward; the project copies them to `[asset] in the Release output.

The displayed speed is KSA's `NavBallData.Speed`. `EnuBody` displays Surface, `Dock` with a target displays Target, and the other KSA frames display Orbit. This preserves KSA's own frame and speed mechanics. The KSP2 click cycle is not wired to KSA's frame control. The active card formats one decimal place and changes from `m/s` to `km/s` at 99,999 m/s and to `mm/s` at 99,999,000 m/s.

The current renderer follows the active side card's uppercase TMP mode style, tiled mode/units backing sprites, and 65-unit right-aligned number field. It applies the prefab's `-5` character spacing and reduces the number font from 23 toward 18 when needed; `7784.4 m/s` therefore remains in m/s and fits the field. The VEL lettering is slightly smaller inside its opaque black strip; the latest screenshot placed its visible left stroke near the border line. The mode and units ink are centered vertically in their backing rows; the number is centered by exported glyph ink bounds within the prefab's value rectangle. The backing tile opacity is raised from the serialized prefab alpha to match the visible squares in the KSP2 screenshot. The frame tint remains unchanged.

The script uses a 112-128 SDF distance transition for the small SB Liquid letters to give them stronger edges; digits retain the earlier 116-140 transition. These conversions approximate TextMeshPro's SDF shader. The results still need an in-game visual comparison.

After a further screenshot comparison, the VEL glyph centers are at `cardLeft+6.5` while the black strip remains at `cardLeft+2..12`; this places the visible upper-left V stroke near the frame's left line. Right alignment of the mode title excludes the final glyph's nonvisual character-spacing advance, and the units text shares the speed number's `cardLeft+75` right edge.

The tested speed selector remains in the source behind the undefined `KSI2_FUTURE_SPEED_CONTROL_SCREEN` symbol. It is marked **FUTURE USE ONLY** and must only be called from a separate advanced controls screen if that screen is adopted. The current flight HUD uses live game telemetry and does not display the selector.

Visual comparison still needs to establish the active card's runtime visibility, exact font antialiasing, the VEL stack spacing and black strip height, and the title/value baselines. The prefab's inactive upper card has a different integer format and should only be implemented if a game capture shows it active.
