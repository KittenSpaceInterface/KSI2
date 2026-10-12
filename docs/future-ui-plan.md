# Future UI plan: experimental flight effects and controls

Mouse throttle control is implemented as described in
[throttle-mouse-control-plan.md](throttle-mouse-control-plan.md); in-game
validation remains. The atmospheric blur prototype is implemented and its
remaining validation is tracked in
[blur-phase1-plan.md](blur-phase1-plan.md). Experimental
switches belong under the **Experimental** section of the KSI2 UI
configuration and default to disabled. Mouse control is off unless its switch
is enabled. Blur uses the current solid background when disabled or
unavailable.

| Future switch | When enabled | Default behavior |
|---|---|---|
| Mouse throttle control | Drag the curved throttle handle to command throttle directly, subject to KSA control authority, time warp, minimum throttle, and automatic burn rules. | Drag control is disabled. The gauge remains read-only. |
| Blurred panel backgrounds | Sample and blur the rendered scene inside the existing panel shape masks. | Draw the current solid-color masked backgrounds. |

## Shared pilot interface layout tree

Replace the current implicit relationships between independent ImGui renderers
with a small KSI2 layout tree. A pilot interface root should own the shared
position and scale. Its children should include the throttle, the navball
cluster, and the atmospheric and vertical-speed indicators. Inside the navball
cluster, the background, ball and markers, dials, readouts, and controls should
use local coordinates relative to that cluster. The background and ball are
siblings under the cluster; the background is a visual layer, not a parent.

Calculate the root transform once per frame and pass resolved child transforms
to each renderer. Dragging the single edit overlay should update the root, and Reset
layout should restore the root's initial transform. Keep visual draw order and
interactive hit areas explicit. Preserve the current appearance and saved
settings while moving renderers to this model, then compare placement at
multiple viewport sizes.

KSA's native gauge definition has a `GaugeCanvas` with a list of sibling
`GaugeComponent`s; KSI2 currently draws through ImGui draw lists. This plan
models parent-child layout within KSI2 rather than assuming ImGui draw lists
automatically inherit transforms. See [screen-layout.md](screen-layout.md)
for the current coordinates.

## UI compositing and input architecture

The earlier pilot artwork used ImGui's viewport foreground draw list. That was
simple, but it bypassed KSA's normal window stack: the pause backdrop did not
dim our interface. The pause-menu implementation moved artwork into a regular,
transparent ImGui window and placed it, the control hit windows, and settings
behind KSA's modal. In-game testing confirmed that the backdrop covers KSI2 and
the pause menu receives clicks. Those results informed the informal ratings of
5/10 for the foreground approach and 7/10 for the first regular-window version;
the numbers are design judgments, not measured performance scores.

The full-screen artwork window passes pointer input through to KSA. A tested
altitude-card prototype draws the card and handles hover and clicks in one
bounded window; moving it across the screen edge no longer turned it black in
the user's test. The speed card, RCS and SAS buttons, and throttle handle now
follow that pattern and still need the same in-game validation. Their windows
capture input over their own bounds. In layout editing mode, the navball
artwork and combined drag hit area now share a bounded canvas and await an
in-game edge test. The throttle body remains click-through outside editing.
KSA windows also
order themselves by focus: among KSA elements, the last clicked one remains on
top. That normal KSA behavior is separate from KSI2's placement. KSI2 still
mixes a shared artwork window with control canvases, so JSON draw order and
ImGui window order can differ where components overlap. `PilotImGui` also moves
KSI2 windows in ImGui's display stack each frame.
This repeated placement is expected to have a small cost with the current
window count, but has not been profiled. Its main concern is the coupling to
ImGui's internal window-order API and the possibility of visual and input order
diverging when KSA windows change focus.

### Practical target: around 9/10

1. Map the exact KSA UI draw, focus, and modal sequence, then use the most stable
   integration point available for ordinary gauges, KSI2 controls, settings, and
   the pause menu. Keep any unavoidable display-stack adjustment in one adapter
   and document when it must run.
2. Define artwork bounds, hit regions, intended click-through areas, and overlap
   priority together for each component. Keep JSON `z` values for ordering
   within KSI2; do not treat them as KSA window priorities.
3. Validate the input and visual contract in game: ordinary KSA controls remain
   usable through noninteractive KSI2 artwork, KSI2 controls take clicks inside
   their hit regions, overlapping controls select the intended target, and pause
   and other KSA modals cover and block KSI2 controls and the layout editor.
   Include KSA focus changes, viewport sizes, and opening and closing menus.
4. Profile window placement and draw-list cost before optimizing it. Change the
   per-frame ordering only if the integration map or measurements show a safer
   or clearer approach.

### Ideal direction: closer to 10/10

A perfect-world integration would give mod gauges a supported KSA compositing
layer with one authoritative order for drawing, focus, hit testing, and modal
behavior. KSI2 could register components and their hit regions declaratively,
without manipulating ImGui's internal window list or depending on call order.
That depends on capabilities KSA may not expose to mods, so 10/10 is a direction
to approach, not a completion criterion for this plan. If those capabilities
remain unavailable, isolate the compatibility code, document its assumptions,
and recheck them when KSA updates.

## Mouse throttle control

The separate `interface-investigation/throttle-indicator-map.md` found no
public KSA setter for an exact throttle value. KSA's supported throttle
up/down input changes the value at `0.7 * delta_time`, so a handle driven by
that path lags behind the pointer. The implemented experimental path writes
KSA's private `_manualControlInputs` field for immediate response. The field
could change in a later KSA version.

Mouse control is opt-in. The gauge reads the throttle value back from KSA and
checks control authority, time warp, minimum throttle, and automatic burn
state before each write. If the private field is unavailable, dragging is
disabled for the session and the reason is shown in the settings window. There
is no alternate input path. In-game input behavior still needs validation.

## Blurred panel backgrounds

The separate `interface-investigation/blur-panel-rendering-investigation.md`
identifies KSP2's scene-sampled `UIBlur2` material on the atmospheric,
vertical-speed, and throttle gauges and the navball cluster. KSI2 uses their
masks with a solid fill by default. The first atmospheric prototype adds a
custom Vulkan draw path with scene texture sampling, shader and pipeline
resources, and cleanup. The original blur kernel and live render
parameters are not fully established, so the initial result would be an
approximation.

Keep blur opt-in. The shared prototype now applies it to all four identified
panel backgrounds when enabled; the broader path still needs in-game
validation. If the required scene texture, shader,
pipeline, or callback is unavailable, draw the solid backgrounds for that
session and report the failure once. Preserve the current masks and panel
visibility behavior in either mode.

### Crash recovery

The blur switch and opacity are saved in `ksi2.settings.json`. Blur remains
disabled by default for a new installation. When blur is enabled, KSI2 writes
`ksi2.blur-active` before saving the enabled setting or creating the blur
renderer. The marker remains for the entire enabled run and is removed only
after normal mod cleanup. If it is present at the next launch, KSI2 saves
`BlurPanelBackgrounds: false` before rendering and reports the recovery in
the settings UI. A crash at any point while blur is enabled therefore resets
the switch on the next launch, including crashes after many rendered frames.

The UI warns that the Vulkan path is experimental. If startup cannot reach
the recovery code or settings cannot be written, manual recovery is to set
`BlurPanelBackgrounds` to `false` in `ksi2.settings.json` and remove
`ksi2.blur-active` from the KSI2 mod folder. If the marker cannot be created,
KSI2 does not enable blur for that run. Normal shutdown is identified by the
mod unload callback; if KSA skips that callback on an otherwise clean exit,
the next launch will safely disable blur.
