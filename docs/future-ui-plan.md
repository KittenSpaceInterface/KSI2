# Future UI plan: experimental flight effects and controls

Mouse throttle control is implemented as described in
[throttle-mouse-control-plan.md](throttle-mouse-control-plan.md); in-game
validation remains. Blurred panel backgrounds remain deferred. Experimental
switches belong under the **Experimental** section of the KSI2 UI
configuration and default to disabled. Mouse control is off unless its switch
is enabled. Blur uses the current solid background when disabled or
unavailable.

| Future switch | When enabled | Default behavior |
|---|---|---|
| Mouse throttle control | Drag the curved throttle handle to command throttle directly, subject to KSA control authority, time warp, minimum throttle, and automatic burn rules. | Drag control is disabled. The gauge remains read-only. |
| Blurred panel backgrounds | Sample and blur the rendered scene inside the existing panel shape masks. | Draw the current solid-color masked backgrounds. |

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
vertical-speed, and throttle gauges and the navball cluster. KSI2 currently
uses their masks with a solid fill. A dynamic KSA implementation would need a
custom Vulkan draw path, scene texture sampling, shader and pipeline resource
management, and correct cleanup. The original blur kernel and live render
parameters are not fully established, so the initial result would be an
approximation.

Keep blur opt-in. Apply it consistently to the identified panels only after
the render path is validated in game. If the required scene texture, shader,
pipeline, or callback is unavailable, draw the solid backgrounds for that
session and report the failure once. Preserve the current masks and panel
visibility behavior in either mode.
