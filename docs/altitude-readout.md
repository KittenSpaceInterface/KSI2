# Altitude readout

`AltitudeReadoutRenderer` draws the active right side card from KSP2's
`GRP-ALT/Container/DataContainer`. It uses the current `NavballClusterLayout`:
the card begins 101 UI units right of the cluster center and 32 units above
it. Its mirrored frame, black ALT strip, LCD tiles, icon, number, and units
are separate layers. The inactive upper `DataContainer (1)`
is not drawn.

The card starts in the altitude mode corresponding to KSA's current navball
frame. Click anywhere on the card to cycle SEA and GROUND, independently of
that frame. SEA reads KSA's `GetBarometricAltitude()`, and GROUND reads
`GetRadarAltitude()`; the card does not change KSA's frame selection. SEA
therefore remains selected when the craft passes over land until the user
cycles it. The active KSP2 side card displays whole numbers, changing
from M to KM at 99,999 m and to MM at 99,999,000 m. The narrower rounding
boundary near 100 m follows KSP2's serialized float threshold.

Prepare the locally extracted artwork with the same AssetRipper export used
for the speed card:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Prepare-SpeedReadoutAssets.ps1 -ExportedAssets '<AssetRipper-export>\ExportedProject\Assets'
```

The script copies the SEA/GROUND icons into ignored
`external asset package/navball/`, along with the shared tile and ALT title atlas. The
number uses KSA's current ImGui font, matching the heading popup. The SEA/GROUND
mode and units use the `LCD_Solid.ttf` face loaded for the RCS/SAS buttons. Build
normally afterward. The source prefab IDs and exact binder behavior are in
`interface-investigation/altitude-readout-investigation.md` in the companion
repository.

The prefab contains a selected mode-bar sprite under the units. In the
available KSP2 reference image it is not visible there, while it produced two
markers over KM in KSI2. The renderer leaves out that bar. The two LCD tile
tints use higher alpha than the serialized prefab to make their squares
visible in KSI2's direct ImGui composition. The card draws after the vertical
speed and atmospheric indicators so its frame and text stay above them.

The final glyph edges, tint, and baselines still need comparison with a KSP2
screenshot at the same viewport and UI scale.
