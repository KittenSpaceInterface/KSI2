# Future UI plan: experimental flight effects and controls

These features are deferred. When implemented, put both switches under an
**Experimental** section of the KSI2 UI configuration and keep each disabled
by default. Save the choices with the other UI settings. A fresh installation,
missing setting, invalid setting, or failed experimental path should use the
fallback listed below. The settings window does not have this section yet;
the throttle gauge currently displays KSA's throttle without changing it.

| Future switch | When enabled | Default and fallback |
|---|---|---|
| Mouse throttle control | Drag the curved throttle handle to command a target throttle directly, subject to KSA control authority, time warp, minimum throttle, and automatic burn rules. | Use the earlier KSA input path, which changes throttle at `0.7 * delta_time` through throttle up/down commands. |
| Blurred panel backgrounds | Sample and blur the rendered scene inside the existing panel shape masks. | Draw the current solid-color masked backgrounds. |

## Mouse throttle control

The separate `interface-investigation/throttle-indicator-map.md` found no
public KSA setter for an exact throttle value. KSA's supported
throttle up/down input changes the value at `0.7 * delta_time`, so a handle
driven by that path lags behind the pointer. The investigated direct path
would write KSA's private `_manualControlInputs` field and clear input flags;
that field could change in a later KSA version, and the write bypasses KSA's
normal rate limit.

For a future implementation, keep direct mouse control opt-in. Read the
accepted throttle value back from KSA for the gauge, enforce KSA's control
and warp restrictions before commands, and release any held throttle up/down
inputs when a drag ends or control becomes unavailable. If the direct path
cannot be used, disable it for the session, report the reason once, and
continue through the `0.7 * delta_time` input path. Do not treat the pointer
target as proof that KSA accepted a throttle change.

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
