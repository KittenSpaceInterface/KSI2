# Pilot UI architecture

`KSI2Mod` forwards StarMap lifecycle calls to `PilotUiController`. The controller
owns the settings controller, combined layout editor, shared blur service, and a
list of modules. A module owns its renderers and declares its asset assignments.
The current modules are throttle, navball, and right indicators. Atmosphere and
vertical speed remain separate components inside the right indicators module.

Each GUI frame reads the throttle snapshot, draws the existing settings window,
and resolves `PilotFrameLayout` once from the viewport and the saved root
position. The controller selects either instrument or layout editing mode and
passes that mode to every module. Module `Update` calls create input hit areas
and update mode or control state before any visual draw calls. The controller
then submits module render entries sorted by `Order`. These entries preserve the
existing visual stack: cluster background, throttle, sphere, side dials, speed
card, heading dial, atmosphere, vertical speed, altitude card, bottom controls.
The speed mode is updated before the sphere renders. Input update order puts
the speed card over the overlapping throttle hit area, as before. Editor input
updates the resolved frame before drawing; its outline draws last.
The speed state follows vehicle and frame changes even while layout editing
blocks instrument clicks. The settings window reads throttle diagnostics and
preview values from the throttle module's state object.

The layout has one root offset, one shared user scale, and explicit throttle and
navball roots. Each module supplies `PilotLayoutDefinition` entries for its
components and the bounds it contributes to the combined drag area. The
resolver stores these bounds by stable ID; it needs no new component fields when
a panel is registered. The editor updates the same normalized `X` and `Y` saved fields. Module
parentage, visibility, editability, and scale remain separate choices, so a
future panel can add independent controls without changing saved JSON fields.

To add a module, implement `IPilotUiModule`, register it in the controller, and
declare its layout definitions and render entries. Mark visible bounds that
should expand the combined editor area. Definitions can use the resolved roots,
viewport, settings, and earlier definitions. Register textures in
`Initialize` through `TextureRegistry`. Give each component the resolved bounds
it needs; renderer code should not calculate layout or read viewport size.
Handle instrument input in `Update`, cancel transient input in
`CancelInteraction`, and release module owned resources in `Dispose`. Shared
textures belong to the KSA texture pipeline; `PanelBackgroundBlur` belongs to
the controller, and the ImGui font atlas belongs to KSA. Native KSA panels need
a separate adapter and investigation before being added.

Compatibility baseline: the default 1920 x 1080 navball root is
`(112.85, 746.95)` and throttle uses its existing 175 x 318 frame, 24 pixel
margin, normalized saved position, and 0.5-2 user scale. The settings window
still offers combined dragging, reset, preview throttle, mouse throttle control,
and guarded experimental blur. `Editing` resets to false on load. The existing
JSON names and meanings are unchanged. Checks cover default and saved positions,
viewport origins, aspect ratios, scale limits, drag cancellation, speed mode
changes while editing, settings reload, and blur marker cleanup.
