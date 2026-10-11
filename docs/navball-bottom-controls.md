# Navball bottom controls

`NavballBottomControls` draws the four items at the bottom of KSP2's navball
cluster. Its coordinates, state colors, and roles come from the investigation
in `interface-investigation/navball-bottom-icons-investigation.md`.

- RCS and SAS are clickable. They call KSA's `FlightComputer.ToggleRCSMode()`
  and `Vehicle.ToggleStabilization()` and read the resulting game state each
  frame. The outline includes the dots above and below the circle.
- The object-cycle glyph is a status indicator, bright blue when more than one
  live vessel is in KSA's current frame and dim blue otherwise. KSA's cycle
  command remains on its existing input binding.
- KSA does not expose KSP2 CommNet status. The signal glyph is green when the
  controlled KSA vehicle is controllable and dim blue otherwise. It reports
  local vehicle control, not a simulated radio link.

RCS and SAS labels are drawn from `LCD_Solid.ttf` through KSA's ImGui font
manager, so they are rasterized at display density rather than scaled from a
small PNG. The existing speed and altitude PNG font atlases are unchanged.

Run `tools/Prepare-BottomControlsAssets.ps1` to copy the locally exported
outline and icon sprites into ignored `external asset package/navball/`. The LCD font is
committed under `src/KSI2/assets/fonts/` and packaged under `assets/fonts/`.
