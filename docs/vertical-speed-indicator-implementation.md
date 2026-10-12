# Vertical-speed indicator implementation

The widget uses the KSP2 vertical-speed prefab artwork and its 85 x 140 layout.
KSP2's serialized `UIValue_ReadNumber_Dial` binder maps -1000 to +1000 m/s,
then evaluates a two-key curve with zero endpoint tangents. The decompiled
`UIValueNumberRange.MapValueTo01` divides by that 2000 m/s range, and
`UIValue_ReadNumber_Dial.RedrawValue` interpolates the needle's position and
rotation from the resulting curve value. The telemetry provider supplies raw
`SurfaceVerticalSpeed`; it sets no replacement number range. This mapping puts
10 m/s less than one widget pixel from zero, although the printed 10 mark is
about 21 widget pixels away. The saved KSP2 binder and number artwork therefore
do not agree as a calibrated speed scale.

KSI2 uses seven speed anchors: two prefab endpoints beyond the printed `-100`
and `+100`, plus five adjustable red guides. The five guides use the same
speed-to-pose calculation used in the original visual calibration, so a saved
slider number restores the same guide location. Within each pair of anchors,
speed maps
linearly to the visible tip's vertical pixel position; the renderer inverts
the prefab's curved needle path to get the required pose. For equal visual
spacing, the nominal speed intervals 0 to 10, 10 to 100, and 100 to 1000 have
relative cost 1:9:90. The path ends have less space beyond the printed 100
marks, so the final intervals use their measured path positions.
The actual speed anchors are always `-1000, -100, -10, 0, 10, 100, 1000` m/s.
The saved calibration numbers place the guides; they are not the actual speeds
needed to reach those guides. The needle and corresponding guide use the same
pose, including the shared rotated offset, until the 2-pixel outward movement
limit applies beyond `+100` m/s. That limit measures only the outward part of
the needle tip's movement from its calibrated `+100` position. Excess movement
shifts the whole needle inward along its own line, toward the lower left of
the screen; travel along the dial remains unchanged. The red guides stay fixed
and the needle keeps its original length.
The serialized endpoint locators are `(59, 131, 5°)` and `(16, 24, 45°)` in
top-down widget coordinates; their parent begins one unit inside the widget's
left edge. The two-unit alignment adjustment now follows the needle rotation.
The gauge remains 8 units above the atmospheric indicator.

The telemetry value is the controlled vehicle's surface velocity projected on
the normalized vehicle position vector:

```text
vertical speed = dot(position CCI / length(position CCI), surface velocity CCI)
```

KSA's `GetSurfaceVelocityCci()` removes the parent body's rotational velocity
from inertial velocity. Projecting that result onto local radial-up excludes
orbital tangential speed from the gauge. Invalid or unavailable vehicle state
hides the widget for that frame. KSP2's `TelemetryApi/VerticalSpeedIndicator`
reads `TelemetryDataProvider`'s `SurfaceVerticalSpeed`, calculated as the dot
product of surface movement velocity and `HorizonUp`. KSI2 currently projects
surface velocity onto the normalized position vector as its closest available
radial-up equivalent.

The `KSI2 Vertical Speed Configuration` window starts closed. The
`Vertical Speed Configuration` button under **Advanced** toggles it, and its
Close button or title-bar X closes it. It has a -150 to +150 m/s preview slider
and an adjacent numeric text box that accepts test speeds from -10000 to +10000
m/s when Enter is pressed. The slider stays at its end when a typed speed is
outside its range, while the text box shows the current test speed. Seven
persistent calibration sliders also have numeric inputs beside them; pressing
Enter applies and saves the typed calibration value. The test speed override
and red number guides turn off when the window closes. The red guides are off
by default. The initial calibration
numbers, bottom to top, are `-10000, -83, -9, 0, +25, +320, +10000`.
The `+320` value for the printed `+100` mark comes from the user's visual
calibration. At `-10000` and `+10000`, the outer guides sit at the authored
path endpoints. Their sliders extend to `-20000` and `+20000` and move the
guides in small steps beyond the authored path. The five inner ticks retain
their earlier speed-to-pose placement calculation.
Changing any mark-speed slider updates the live needle mapping immediately;
values are saved in `ksi2.settings.json`
after each edit. A shared offset slider starts at the user's measured 2 pixels
and moves both the needle and ticks in the needle's rotated local vertical
direction. No manual X or Y adjustment is needed.

The backing, color band, number artwork, border, bracket, arrow, and shade use
the serialized KSP2 sprites and prefab tint values. The widget root flips
vertically: the color band and numbers have a compensating child flip, while
the backing, border, bracket, arrow, and shade retain the root flip. The arrow
is clipped to the prefab's `Atmo-MSK-Needle` UI mask. The color and number sprite
PNGs are local ignored assets copied from the focused AssetRipper export; the
renderer reuses the existing atmosphere backing, border, bracket, arrow, and
shade textures. Runtime visual comparison in KSA remains open.
