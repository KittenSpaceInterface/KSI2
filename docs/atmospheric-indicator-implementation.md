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
moving needle, rocket and thrust sprites, then the shade. Colors and the
bottom-left `(373, 87)` widget origin follow the inspected prefab. The old
`(423, 137)` estimate omitted the 50-unit centered pivot of the Container.
The needle uses 2-unit cells and the rocket uses 1-unit cells, with bilinear
mask sampling. This approximates Unity's UI stencil while keeping the moving
rocket inside the gauge artwork. The marker
sprite `VertSpeed-ELE-Needle` is shared with the vertical-speed widget, so
there are nine source PNGs in the local `[asset package]` directory.

The PNGs were copied from the existing focused AssetRipper export of
`hudinstruments_assets_all.bundle`. They stay in ignored `external asset package/atmosphere`
and are copied to the Release output only on machines that have them. No
proprietary source artwork is committed. Runtime visual comparison at multiple
gauge values and viewport sizes remains open; the current validation covers
the Release build and focused geometry/telemetry checks.
