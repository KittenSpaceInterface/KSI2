# Pilot layout JSON

`src/KSI2/Layout/PilotLayoutDefaults.json` is the packaged source for the
pilot interface's structural positions and KSI2 draw order. The Release build
copies it beside `KSI2.dll` as `ksi2.layout.defaults.json`. Edit the source file
to change defaults for everyone. Changes take effect the next time KSI2 loads.
`designSize` supplies the reference viewport used to scale its coordinates.

Root elements use a viewport anchor. `viewport-bottom-left` places the root's
bottom-left corner at the viewport's bottom-left corner, then applies the
`offset` in scaled design units. For example, `[24, -24]` leaves a 24 unit
margin at the left and bottom. `viewport-top-left` uses the viewport's
top-left corner and the root's top-left corner. Roots are kept within the
viewport. Each child offset is measured from its parent's top-left corner and
can be negative. Child positions are not clipped to the parent's rectangle.
The tree expresses which elements move together; `z` gives the global
back-to-front order for the draw layers and their input windows. Values must
be unique and below 2000, which is reserved for the layout editor. `size`
describes the current artwork's bounds and should remain unchanged unless its
renderer is updated too.

For personal changes, create `ksi2.layout.json` beside `KSI2.dll`. It contains
only changed anchors, offsets, or draw orders. For example:

```json
{
  "elements": {
    "navball-cluster": {
      "children": {
        "heading-dial": {
          "offset": [4, 0],
          "children": {
            "heading-readout": { "offset": [117, -30] }
          }
        },
        "navball-sphere": { "z": 1250 }
      }
    }
  }
}
```

The override file is not rewritten while editing. KSI2 loads both files once
on startup and keeps the resolved layout in memory. The existing in-game
combined drag remains a separate normalized viewport position in
`ksi2.settings.json`; its saved position takes priority over the throttle's
default anchor offset. Use **Reset layout** in the settings window to clear
that saved drag position before adjusting the throttle's JSON offset. Reset
layout does not delete `ksi2.layout.json`.

The layout file controls structural elements such as the cluster background,
navball sphere, cards, heading dial and readout, right indicators, and bottom
controls. Moving a parent moves its children. Flight-driven motion, including
rotating dials and gauge markers, remains in geometry code.
