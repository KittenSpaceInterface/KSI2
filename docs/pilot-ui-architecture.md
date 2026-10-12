# Pilot UI architecture

`KSI2Mod` forwards StarMap lifecycle calls to `PilotUiController`. The controller
owns the settings controller, combined layout editor, shared blur service, and a
list of modules. A module owns its renderers and declares its asset assignments.
The current modules are throttle, navball, and right indicators. Atmosphere and
vertical speed remain separate components inside the right indicators module.

Each GUI frame reads the throttle snapshot, draws the existing settings window,
and resolves `PilotFrameLayout` once from the viewport, the loaded layout JSON,
and the saved root position. The controller selects either instrument or layout
editing mode. Each module declares `PilotUiLayer` entries with one stable
`PilotLayerOrder` identifier for its artwork
and optional input update. The controller sorts the entries once, updates early
input state in back-to-front order, then visits draw layers in the same order.
Control canvases process their input and draw their artwork during that visit.
They are raised as they are drawn, so an overlapping upper interactive layer
receives the pointer. Layers without input let the pointer pass through.
The default JSON preserves the visual stack: cluster background, throttle,
sphere, side dials, speed card, heading dial, heading readout, atmosphere,
vertical speed, altitude card, bottom controls.
The speed mode is observed before the sphere renders; a click on the card
changes the sphere on the next frame. Canvas order puts the speed card over the
overlapping throttle hit area. While editing, the layout editor owns a bounded
canvas for both its drag hit area and the navball artwork; its input updates the
resolved frame before drawing, and its outline is appended after the draw
layers. Outside editing, noninteractive artwork draws inside a transparent,
input-free ImGui window. The speed and altitude cards, RCS and
SAS buttons, and throttle handle draw their artwork in their own tightly
bounded ImGui windows, which also own their mouse hit areas. The throttle body
remains on the shared artwork canvas. Settings
are a separate regular ImGui window. KSI2 places these windows above ordinary
KSA gauges and below an open KSA modal, so the pause backdrop dims the entire
pilot interface and the pause menu receives clicks. KSI2 cancels instrument and
editor interaction while a modal is open. Layer numbers order KSI2's draw calls;
cross-window overlap also follows ImGui window placement, not KSA popup order.
The cluster background starts at 1000, leaving 0-999 for future KSI2 underlays.
Most layer values are 100 apart to leave room for intermediate components.
KSA has no matching layer value or default zero; ordering KSI2 relative to KSA
requires window placement, not a lower `PilotLayerOrder`.
The speed state follows vehicle and frame changes even while layout editing
blocks instrument clicks. The settings window reads throttle diagnostics from
the throttle module's state object.

The packaged `ksi2.layout.defaults.json` defines the throttle and navball
cluster roots, their viewport anchors, child offsets, artwork bounds, and draw
orders. A sparse `ksi2.layout.json` beside the DLL can override positions and
draw orders. The files are loaded once at startup. The resolver stores all
node bounds by stable ID and unions drawn node bounds for the combined drag
area. Modules can still supply `PilotLayoutDefinition` entries for calculated
extensions. The editor updates the existing normalized `X` and `Y` settings
fields for combined dragging; individual JSON offsets are independent of that
drag state. See `pilot-layout-json.md` for the file format.

To add a module, implement `IPilotUiModule`, register it in the controller, and
declare its layout definitions and layers. Give each layer a stable
`PilotLayerOrder` identifier, a matching node with a distinct `z` value in the
layout JSON, and pair input and drawing in that layer. Definitions can use the
resolved roots, viewport, settings, and earlier definitions. Register textures in
`Initialize` through `TextureRegistry`. Give each component the resolved bounds
it needs; renderer code should not calculate layout or read viewport size.
Handle instrument input in the layer's `UpdateInput`, cancel transient input in
`CancelInteraction`, and release module owned resources in `Dispose`. Shared
textures belong to the KSA texture pipeline; `PanelBackgroundBlur` belongs to
the controller, and the ImGui font atlas belongs to KSA. Native KSA panels need
a separate adapter and investigation before being added.

Compatibility baseline: the default 1920 x 1080 navball root is
`(112.85, 746.95)` and throttle uses its existing 175 x 318 frame, 24 pixel
margin, normalized saved position, and 0.5-2 user scale. The settings window
still offers combined dragging, reset, mouse throttle control,
and guarded experimental blur. `Editing` resets to false on load. The existing
JSON names and meanings are unchanged. Checks cover default and saved positions,
viewport origins, aspect ratios, scale limits, drag cancellation, speed mode
changes while editing, settings reload, and blur marker cleanup.
