# Velocity readout

`SpeedReadoutRenderer` draws the active `GRP-VEL/Container/DataContainer` side card from KSP2's `group_navball.prefab`. Its position follows `NavballClusterLayout`, so the two existing side dials and any future navball placement change move with it. The alternate `DataContainer (1)` above the cluster center is inactive in the saved prefab and is not drawn.

The KSP2 readout uses separate TextMeshPro fields for `VEL`, the mode title, speed digits, and units. KSI2 draws the number with KSA's current ImGui font, matching the heading popup. The mode title, units, and vertical VEL title use the `LCD_Solid.ttf` face already loaded for the RCS/SAS buttons.



The speed card follows KSA's current navball frame: `EnuBody` selects Surface, `Lvlh` selects ORBT, `VlfBody`, STAR (`EclBody`), and BURN (`BurnBody`) select OVEL, and `Dock` with a target selects Target. Clicking the card cycles Surface → ORBT → OVEL → Target → Surface, skipping Target when no target exists, and selects KSA's corresponding SURF, ORBT, OVEL, or TGT frame. The displayed speed uses KSA's surface, inertial, or target-relative velocity for that selected mode. ORBT and OVEL share the orbital navball texture. The card's black backing covers the side dial beneath it. The active card formats one decimal place and changes from `m/s` to `km/s` at 99,999 m/s and to `mm/s` at 99,999,000 m/s.

The current renderer keeps the active side card's uppercase mode text, tiled mode/units backing sprites, and 65-unit right-aligned number field. The ImGui number font starts at size 23 and shrinks toward 18 when needed; `7784.4 m/s` therefore remains in m/s and fits the field. The VEL lettering is slightly smaller inside its opaque black strip; the latest screenshot placed its visible left stroke near the border line. The LCD mode and units text are centered vertically in their backing rows and shrink to fit the row width; the number's ImGui text line is centered in the value rectangle. The backing tile opacity is raised from the serialized prefab alpha to match the visible squares in the KSP2 screenshot. The frame tint remains unchanged.

After a further screenshot comparison, the VEL glyph centers are at `cardLeft+6.5` while the black strip remains at `cardLeft+2..12`; this places the visible upper-left V stroke near the frame's left line. The mode title is right-aligned to `cardLeft+63`, and the units text shares the speed number's `cardLeft+75` right edge.

The tested speed selector remains in the source behind the undefined `KSI2_FUTURE_SPEED_CONTROL_SCREEN` symbol. It is marked **FUTURE USE ONLY** and must only be called from a separate advanced controls screen if that screen is adopted. The current flight HUD uses live game telemetry and does not display the selector.

Visual comparison still needs to establish the active card's runtime visibility, exact font antialiasing, the VEL stack spacing and black strip height, and the title/value baselines. The prefab's inactive upper card has a different integer format and should only be implemented if a game capture shows it active.
