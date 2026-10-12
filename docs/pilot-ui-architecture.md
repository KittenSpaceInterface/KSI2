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
and optional input update. The controller sorts the entries once, updates their
input in back-to-front order, then draws their artwork in the same order. Input
windows are raised as they are updated, so an overlapping upper interactive
layer receives the pointer. Layers without input let the pointer pass through.
The default JSON preserves the visual stack: cluster background, throttle,
sphere, side dials, speed card, heading dial, heading readout, atmosphere,
vertical speed, altitude card, bottom controls.
The speed mode is updated before the sphere renders. Input update order puts
the speed card over the overlapping throttle hit area. The layout editor is the
final interactive layer while editing; its input updates the resolved frame
before drawing, and its outline draws last. Settings remain a separate ImGui
window. Pilot artwork uses the viewport foreground draw list, so layer numbers
describe order within KSI2, not the order of KSA popups.
The cluster background starts at 1000, leaving 0-999 for future KSI2 underlays.
Most layer values are 100 apart to leave room for intermediate components.
KSA has no matching layer value or default zero; placing KSI2 artwork behind KSA
would require a change to the ImGui draw channel, not a lower `PilotLayerOrder`.
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
