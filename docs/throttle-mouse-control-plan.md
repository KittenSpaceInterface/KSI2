# Mouse throttle control implementation plan

The code path and settings are implemented. Release builds compile against
the installed KSA assemblies. The in-game validation checklist below remains
open, especially keyboard interaction during a drag.

## Intended behavior

Dragging the throttle handle commands the controlled vehicle's manual throttle.
The green fill, handle, and number continue to show KSA's accepted throttle
value. The gauge remains display only when no controllable vehicle is present,
the game locks flight controls, layout editing or preview is active, or the
flight computer controls the burn.

Mouse dragging is enabled by default. Add an **Experimental** section to the
existing KSI2 settings window with a **Mouse throttle control** checkbox,
saved as `MouseThrottleControl` and defaulting to `true`. When enabled, a drag
writes the private throttle input register for immediate response. If that
path fails or its KSA field is unavailable, disable mouse control for the
session and show the reason. There is no alternate input path.

## Work sequence

1. **Separate input from rendering.** Keep `ThrottleGeometry` responsible for
   the curved handle path. Add a controller that owns the active drag, target,
   selected vehicle, active KSA input direction, and session fallback state.
   Keep `ThrottleGauge` responsible for ImGui hit testing and drawing.
2. **Map pointer position to throttle.** Start a drag only on a padded handle
   hit area and only with the left mouse button. Convert pointer Y from screen
   coordinates through the gauge's current origin and scale. Invert
   `ThrottleGeometry.HandleCenter(t).Y` on `[0, 1]` with a bounded search so
   the pointer follows the actual curved path, then clamp to
   `[vehicle.GetMinThrottle(), 1]`. Maintain ImGui active-item capture until
   release, including when the pointer leaves the handle.
3. **Write the throttle directly while dragging.** Read the private
   `_manualControlInputs` field only inside a narrow, guarded adapter. Clamp
   the target and write `EngineThrottle` each frame. The implementation leaves
   keyboard throttle flags intact; validate how a held keyboard throttle key
   interacts with direct writes.
4. **Add settings and user feedback.** Persist the opt-in checkbox in
   `ksi2.settings.json`, retaining `false` for old or invalid settings.
   Disable dragging during layout editing and preview. If the field is missing
   or incompatible, stop mouse control for the session and explain why; do
   not switch to another control path.

## Control and lifecycle rules

- Recheck the controlled vehicle, `IsDisposed`, `IsControllable`, simulation
  speed, minimum throttle, and automatic burn state before every command.
- Confirm the behavior of `Program.IsControlledVehicleActive` and ImGui input
  capture in game. KSA's `Vehicle.PrepareWorker` can clear held player input
  when the controlled vehicle is inactive, keyboard capture is active, or
  simulation speed exceeds 30x. The controller must recover without leaving
  stale pressed state.
- Keep the visible gauge tied to `GetManualThrottle()` after each command.
  Pointer target is a command, not confirmed telemetry. The normal path may
  visibly lag behind the pointer by design.
- Preserve layout dragging as a separate mode. A mouse gesture must never
  reposition the gauge and command a vehicle at the same time.
- Do not change throttle from the preview slider or when the gauge is hidden.
- Run drag cleanup before the early returns in `ThrottleGauge.Draw()` and from
  `ThrottleGauge.Dispose()`, which `KSI2Mod.Unload()` already calls. The current
  early returns for hidden UI and missing telemetry would otherwise skip a
  pending release.

## Validation before enabling instant mode

1. Check pointer-to-value endpoints, minimum-throttle clamping, and hit area
   at several UI scales and viewport positions.
2. Exercise the normal path in game at 0%, 50%, and 100%, including reversing
   direction during a drag and releasing outside the gauge. Confirm the KSA
   rate and that held actions are always released.
3. Exercise vehicle switching, loss of control, automatic burn, high time
   warp, hidden UI, layout mode, and mod unload during a drag.
4. Test direct mode against the installed KSA build. Confirm exact values,
   keyboard interaction, and accepted telemetry. Keep the setting off by
   default after validation.

Source findings: `interface-investigation/throttle-indicator-map.md` in the
separate investigation repository. The current gauge input and geometry live
in `src/KSI2/ThrottleGauge.cs` and `src/KSI2/ThrottleGeometry.cs`.
