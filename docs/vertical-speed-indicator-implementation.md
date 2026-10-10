# Vertical-speed indicator implementation

The widget uses the KSP2 vertical-speed prefab artwork and its 85 x 140 layout.
Its dial ranges from -1000 to +1000 m/s. The binder's zero-tangent animation
curve eases the arrow between the serialized endpoint locators: `(59, 131, 5°)`
and `(16, 24, 45°)` in top-down widget coordinates. The one-unit X offset
comes from the stretched locator parent. The gauge is placed 8 units above the
atmospheric indicator and shares its horizontal position, so both stay aligned
when the navball cluster moves.

The visible arrow is offset down by two widget units to align with the zero
tick in the imported artwork. This is a visual adjustment after in-game
inspection; the prefab's telemetry mapping and locator path stay unchanged.

The telemetry value is the controlled vehicle's surface velocity projected on
the normalized vehicle position vector:

```text
vertical speed = dot(position CCI / length(position CCI), surface velocity CCI)
```

KSA's `GetSurfaceVelocityCci()` removes the parent body's rotational velocity
from inertial velocity. Projecting that result onto local radial-up excludes
orbital tangential speed from the gauge. Invalid or unavailable vehicle state
hides the widget for that frame.

The backing, color band, number artwork, border, bracket, arrow, and shade use
the serialized KSP2 sprites and prefab tint values. The widget root flips
vertically: the color band and numbers have a compensating child flip, while
the backing, border, bracket, arrow, and shade retain the root flip. The arrow
is clipped to the prefab's `Atmo-MSK-Needle` UI mask. The color and number sprite
PNGs are local ignored assets copied from the focused AssetRipper export; the
renderer reuses the existing atmosphere backing, border, bracket, arrow, and
shade textures. Runtime visual comparison in KSA remains open.
