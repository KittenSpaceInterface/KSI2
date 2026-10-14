# Atmospheric indicator implementation

The telemetry adapter reads the controlled
vehicle's `PhysicsEnvironment.AtmosphericDensity` and the nearest body's
`PhysicalAtmosphereReference.SeaLevelDensity`. It returns zero for vacuum or
an airless body and applies the KSP2 fourth-root normalization otherwise.

The geometry uses the two sets of three locators in the sibling investigation
repository's `atmospheric-indicator-map.md`. Positions follow the quadratic
Bezier equivalent of Unity's nested `Vector3.Lerp` calls. Rotation follows
clamped quaternion interpolation between endpoint angles. Both poses convert
Unity's upward Y to ImGui's downward Y. The rotated mask parent then maps the
paths to top-down widget positions: the rocket moves from roughly `(75.5, 19.5)`
in vacuum to `(25.6, 129.4)` at sea level on the 85 x 140 widget. The saved
quaternions turn in the opposite direction from their Euler hint text, so the
port interpolates the saved quaternion directions.

`AtmosphericIndicatorRenderer` draws the backing, layers, border, bracket,
moving needle, rocket and thrust sprites, then the shade. In the KSP2 prefabs,
the navball cluster is at bottom-left `(94, 82)` and the atmosphere widget is
at `(373, 87)`, a `(279, 5)` offset. KSI2 applies that offset to its actual
navball cluster position, so the indicator tracks the relocated navball. At
1920 x 1080 its widget top-left is `(391.85, 901.95)`. The older direct canvas
origin `(373, 87)` put it 18.85 units left and 48.95 units high relative to
the rendered navball.
The needle uses 2-unit cells and the rocket uses 1-unit cells, with bilinear
mask sampling. This approximates Unity's UI stencil while keeping the moving
rocket inside the gauge artwork. The marker
sprite `VertSpeed-ELE-Needle` is shared with the vertical-speed widget. The
vertical-speed widget draws its own color bands and numeric labels. Runtime
visual comparison at multiple
gauge values and viewport sizes remains open; the current validation covers
the Release build and focused geometry/telemetry checks.
