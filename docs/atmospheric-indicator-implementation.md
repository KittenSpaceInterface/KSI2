# Atmospheric indicator implementation

The first implementation step is the live telemetry adapter and both marker
paths in `AtmosphericIndicatorGeometry.cs`. The adapter reads the controlled
vehicle's `PhysicsEnvironment.AtmosphericDensity` and the nearest body's
`PhysicalAtmosphereReference.SeaLevelDensity`. It returns zero for vacuum or
an airless body and applies the KSP2 fourth-root normalization otherwise.

The geometry uses the two sets of three locators in the sibling investigation
repository's `atmospheric-indicator-map.md`. Positions follow the quadratic
Bezier equivalent of Unity's nested `Vector3.Lerp` calls. Rotation follows
clamped quaternion interpolation between endpoint angles. Both poses convert
Unity's upward Y to ImGui's downward Y.

The rendering portion is pending the eight `Atmo-*` sprites listed in that
map. They must be extracted as local assets from the inspected KSP2 bundle and
kept out of version control. No placeholder visuals are registered in the mod,
so this branch does not yet draw an atmospheric indicator. The next rendering
step is to composite the static layers, two moving markers, and foreground
shade, then compare placement at 1920 x 1080 against the authored origin.
