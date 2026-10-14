# Blurred panel backgrounds: phase 1 prototype

## Goal and scope

The first prototype targeted the atmospheric gauge background shape. The shared render path now also covers the throttle, vertical-speed,
and navball-cluster backing shapes identified in the KSP2 investigation.
Borders, needles, readouts, and other foreground layers retain their order.

This is an opt-in prototype. The switch and opacity are saved in
`ksi2.settings.json`; a new installation starts with blur off. A marker stays
on disk throughout a blur-enabled run. If the run ends without normal mod
cleanup, the next launch disables blur before rendering. See
[future-ui-plan.md](future-ui-plan.md) for the recovery behavior.

## Evidence and constraints

- KSP2's `UIBlur2` samples a blurred scene image for color and uses the panel
  sprite for alpha. The atmospheric background is the panel shape mask.
  The KSP2 blur kernel and live render parameters are not fully known, so this
  phase targets the behavior rather than a pixel-identical copy. See the
  separate `interface-investigation/blur-panel-rendering-investigation.md`.
- The inspected KSA render sequence transitions the offscreen scene color to
  sampled-read state before final composition and ImGui drawing. KSA exposes
  `Program.OffscreenTarget`, and its native `ImGauge` uses an ImGui draw
  callback. These are investigation findings, not yet a validated mod API.
  Recheck the installed KSA assemblies before implementing the callback.
- Use KSA's current frame and active command buffer. Do not create or submit a
  separate command buffer or insert image-layout transitions inside an ImGui
  callback. Create and destroy shader, pipeline, and descriptor resources at
  appropriate renderer lifecycle points, including resize and unload.
- Use standard Vulkan/SPIR-V features supported by KSA's device; do not use
  AMD- or NVIDIA-specific extensions. A successful test on one driver does
  not establish compatibility with another.

## Implementation sequence

1. **Verify the hook.** Trace the installed build from scene rendering through
   the sampled-read barrier, final composite, and `ImGuiBackend.Vulkan.RenderDrawData`.
   Confirm how `ImGauge` obtains the active render state, how a mod can access
   the offscreen image view and sampler, and what must be restored after a
   callback. Stop the prototype if these cannot be done without taking over
   KSA's render target or command-buffer lifecycle.
2. **Prove scene sampling.** Insert a callback at the atmospheric background
   layer only. Draw the existing panel mask using a single unblurred sample of
   the scene color. Match screen coordinates, viewport scaling, image alpha,
   tint, and KSA's HDR-to-display color handling. Confirm the rest of the HUD
   still renders after the callback.
3. **Add the blur kernel.** Extend the fragment shader to use a small fixed
   set of samples around each covered panel pixel. Choose radius in framebuffer
   pixels so UI scale and resolution changes remain predictable. Clip through
   the original shape mask and keep sampling inside the valid scene image.
4. **Connect the Experimental switch.** Save the opt-in choice with a crash
   marker that is cleared on normal unload. When off, draw the current solid `KSI2AtmoFill` layer. When on
   and all resources are ready, draw the blurred layer in its place. If setup
   fails, keep the solid layer and report the error once. GPU device loss may
   terminate KSA before a fallback can run; a leftover marker disables blur
   on the next launch.
5. **Validate before expanding.** Check KSA startup and exit, repeated toggles,
   viewport resize, UI scale changes, flight scene changes, and mod unload.
   Inspect Vulkan validation output and compare a moving scene behind the
   panel. Run the same prototype on AMD and NVIDIA before declaring the render
   path portable. Record image and performance observations in the blur
   investigation note.

## Phase 1 completion criteria

- With the switch off, the atmospheric background looks as it does today.
- With it on, terrain or sky moving behind the atmospheric panel changes the
  panel color while its silhouette and foreground details remain aligned.
- Enabling, disabling, resizing, and unloading leave other ImGui drawing and
  KSA rendering intact on the tested device.
- A missing scene image or failed pipeline setup selects the solid background.
- A fresh launch retains a cleanly saved blur choice. A previous unclean exit
  disables blur before rendering. Cross-vendor validation remains.

## Prototype status

The shared implementation is in `PanelBackgroundBlur.cs` and the two
`assets/shaders/panel-blur.*` shaders. It samples KSA's scene color in KSA's
existing ImGui render pass, applies a fixed 25-sample blur, and uses the
original background textures as alpha masks. The Experimental control
is persistent and defaults off for new settings. The solid fill is queued when blur setup
fails; an unexpected render callback failure may leave the panel empty for
that frame. The
Release build compiles against the installed KSA assemblies and has been
installed in the local mod folder.

The user confirmed that the initial prototype renders in game, but reported
that it looks too transparent. A source audit found the initial RGB multiplier
was 0.55–0.65 instead of KSP2's recorded `#24262F` (0.141–0.184), and the
prototype incorrectly reduced mask alpha to 92%. An intermediate correction
used the recorded RGB tint and mask alpha directly, matching the decoded
UIBlur2 fragment output, but looked almost solid in KSA. The current
prototype keeps the recorded RGB tint and applies configurable opacity to the mask.
It draws no solid fill underneath successful blur. An Experimental opacity
slider appears directly below the enabled switch. It starts at 99.5%, adjusts
in exact 0.25 percentage point steps via integer ticks, and is saved.
This opacity is an intentional KSI2 visual choice:
KSP2's serialized UI alpha of 0.992 is not an output-opacity multiplier in
the decoded shader. Blur offsets use display pixels so changing scene
resolution does not change the visible radius.

The corrected visual result, resize behavior, unload path, and GPU
portability still require in-game validation. The shader currently
uses an approximate HDR-to-display mapping; compare it with KSA's active
tonemapper during visual review. No AMD/NVIDIA compatibility claim is made
from the successful C# build.

Remaining differences from KSP2: a single 25-sample shader replaces its
screen-copy and horizontal/vertical blur passes; the original blur kernel and
live overrides remain unverified. KSI2 blurs HDR samples before applying its
fixed Reinhard/gamma approximation, whereas KSP2 captures the camera target at
`AfterEverything`; exact capture color space and processing order still need
confirmation. KSI2 does not yet follow KSA's selected tonemapper, exposure,
or gamma. The configurable opacity also differs from the decoded UIBlur2
output. This is not a pixel-identical recreation.

All four backgrounds use one Vulkan pipeline and a single shader pair. Each
distinct mask has its own descriptor set so callbacks queued in the same frame
retain the correct mask; the atmospheric and vertically flipped speed panels
share one mask. On resize, the shared scene image binding is refreshed for all
sets after waiting for prior GPU work. When the blur switch is off or setup
fails, each panel draws its existing solid fill. The expanded set still needs
in-game visual, resize, unload, and cross-vendor validation.
