# SAS mode controls

The enabled `ModeControls` in `group_flightcontrol.prefab` is 184 × 56 UI
units. Its options group is 168 × 32, top inset 8, with four 32 × 32 buttons
at x = 16, 56, 96, and 136. Each icon is centered at 24 × 24.

The KSP2 prefab references `Frame-Fill-6px_0` for its root, with a 12-pixel
9-slice border and tint RGBA `(0.14117648, 0.14901961, 0.18431373,
0.99215686)`. In KSI2, the pilot panels use the orbital readout's
`Frame-6px-rad-fill_0` instead: a `#030B19` tinted fallback, or the same
9-sliced blur mask when blur is enabled. The SAS control follows those
existing KSI2 backgrounds. Its inset border and bracket use the same
14-pixel 9-slice sprites as the orbital readout, tinted `#292B66` and
`#5257CC`. The title cuts into the lower frame like the orbital readout
title, with a matching tinted or blurred backing. KSI2 draws `SAS.CONTROL`
with the same LCD text renderer and blue-violet tint.

The active icon sprites are `ICO-FlightControl-Lock`,
`Flight-ICO-Maneuver_0`, `Flight-ICO-Target_0`, and
`Flight-ICO-AntiTarget_0`. Their 32 × 32 backings use `BG-ICO-Border` at
rest, `Button-Digital-Small-Outline` on hover, and
`Button-Digital-Small-Solid` when selected. The prefab's shared transition
colors are:

| State | Backing | Icon |
| --- | --- | --- |
| Rest | `#555966` | `#AAB2CC` |
| Hover | `#AAB2CC` | `#808599` |
| Selected | `#00FF66` | `#0F1014` |
| Disabled | `#555966` | `#555966` |

KSI2 queues Stability Assist as a `VehicleReferenceFrame` input for the
current navball frame. KSA applies that input as `RateHold` and refreshes the
flight computer. Maneuver maps to the positive Delta V attitude target,
Target to `Toward`, and Anti-target to `Away`. Selection and motion still
need an in-game check.

## Reference popup preview

Right-clicking the Stability Assist lock opens an 88 × 144 panel above it.
The preview uses the same sliced fill, optional blur, border, and bracket as
the SAS control. A centered `MODE` LCD caption interrupts the top frame with
the same backing and spacing as `SAS.CONTROL` at the bottom of the SAS panel.
The popup has two columns of three 32 × 32 button backings. Labels have 4 units
of horizontal text inset and are centered horizontally and vertically, with
no vertical inset. The left column reads `OVEL`, `ORBT`, `SURF`; the right
column reads `TGT`, `BURN`, `STAR`. The selected backing follows the active
flight-computer rate-hold frame for each label. Clicking an enabled button
plays the SAS enable sound and queues its matching reference frame. A second
right-click on the lock or a click outside closes the popup.
