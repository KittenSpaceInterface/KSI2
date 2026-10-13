# Screen placement in the current pilot interface

KSI2 renders Unity UI artwork in KSA's top-left-origin ImGui viewport. The
current navball placement is defined in `PilotLayoutDefaults.json` and
shared by the cluster background, sphere, heading dial, and side dials. These formulas
describe the default KSP2 prefab placement in KSI2's viewport.

## Current cluster calculation

The KSP2 reference canvas is 1920 × 1080. KSI2 uses a uniform scale:

```text
s = min(viewportWidth / 1920, viewportHeight / 1080)
clusterLeft = viewportX + 94 × s
clusterTop = viewportY + viewportHeight - (82 + 300) × s
clusterCenter = (clusterLeft + 150 × s, clusterTop + 150 × s)
```

The live calculation multiplies `s` by the saved Scale control and limits the
panel to the viewport. A saved edit position adds the throttle's displacement
from its initial anchor to the cluster position. In edit mode, one invisible
overlay covers the complete interface and drags all components together.

KSP2's `group_navball / Container` is 300 × 300, bottom-left anchored at
`(94, 82)` on its reference canvas. Its centered sphere is `(244 × s, 232 × s)`
from the viewport's bottom-left. KSP2's visible throttle widget begins at
`(30.15, 86.95)`. KSI2's throttle artwork is inset `(25, 14)` into its
175 × 318 frame, so its default frame origin is `(5.15, 72.95)` from the
viewport's bottom-left. Both defaults were translated by the same
`(-18.85, +48.95)` canvas-unit displacement, matching the combined layout
editor's movement. A saved combined edit position still translates both
instruments together.

## Convert a prefab child to screen coordinates

Unity's local Y increases upward; ImGui screen Y increases downward. For a
child centered on the navball with Unity anchored offset `(u, v)` and size
`(w, h)` in UI units:

```text
screenCenter = (clusterCenter.X + u × s, clusterCenter.Y - v × s)
screenTopLeft = screenCenter - (w × s / 2, h × s / 2)
```

The sphere is 176 × 176 at `clusterCenter`. The heading mask, dial layers, and
frame use 296 × 296 centered rectangles, so their unscaled top-left is 2 units
inside the cluster square. The heading readout is 62 × 40, horizontally
centered, with its bottom 132 units above the cluster center. In screen space
its top-left is `(center.X - 31 × s, center.Y - 172 × s)`.

The velocity and altitude side dials use centered 296 × 296 rectangles. Their
authored arcs face opposite sides because their prefab parents rotate the
artwork by opposite quarter-turns. KSI2 applies those rotations and clips the
artwork and live labels to the shared mask. The readout cards above the dials
have separate child transforms and remain a later implementation pass.

For a separate bottom-left-anchored widget, use the difference between its
KSP2 reference position and the navball's reference position. The atmosphere
widget is at `(373, 87)`, so its offset from the navball is `(279, 5)`:

```text
widgetLeft = clusterLeft + (373 - 94) × s
widgetTop = clusterTop + (300 - (87 - 82) - widgetHeight) × s
```

The vertical-speed widget then sits one widget height plus an 8-unit gap above
the atmosphere widget. The Scale control changes the throttle and all navball
components together. Edit layout moves the complete interface through its
single overlay; Reset layout restores the initial anchors.

The active orbital readout is a separate 240 × 56 viewport-anchored widget.
Its default left edge is `viewportX + 122 × s` and its top edge is
`viewportY + viewportHeight - 72 × s`. It has its own `orbital-readout`
layout root, so overriding the navball position does not move it. A saved
combined layout edit translates both roots by the same displacement.

## Where this calculation applies

Use the actual prefab anchor, pivot, parent size, rotation, and canvas scaling
for each new component. The center formula above applies to centered children
of the 300 × 300 navball cluster. Other anchors, stretched rectangles, nested
rotations, and independently positioned HUD panels require their own
conversion. Sprite texture dimensions and Sprite rects affect sampling and
appearance; a 592-pixel dial source displayed in a 296-unit rect does not
change the rect's screen position.

The KSP2 source geometry and bindings for the two side dials are recorded in
the sibling investigation repository's `side-dials-investigation.md`.

For the separate problem of reducing source artwork into these screen-space
rectangles, including the small navball status icons, see
[Raster artwork at pilot UI scale](raster-scaling.md).
