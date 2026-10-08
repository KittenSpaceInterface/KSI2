# Throttle rendering and coordinate audit

## Evidence

The local `group_throttle.prefab`, `UIPathContainer`, `UISliderPathIndicator`,
Unity UI `Image`, `CanvasScalerExtended`, and KSA `ImGuiBackendGlfwImpl` were
inspected. Extracted game source/binaries are not distributed with the mod.

- The widget's RectTransform is 85 × 290. Texture texels, sprite coordinates,
  RectTransform units, ImGui display coordinates, and framebuffer pixels are
  distinct spaces. Sprite pixels-per-unit is 200; that is not a global screen
  scale. Explicit UI rectangles determine the displayed size.
- The exported layer PNG canvases are 170 × 580. The current CPU reconstruction
  retains those complete canvases at 85 × 290, including transparent pixels.
  It no longer stretches tight sprite crops independently. The source manifest
  retains crop rectangles, pivots and offsets for further Unity UV comparison.
- The radial Image is **Radial360**, clockwise, origin Bottom, fill amount
  `throttle / 4`. It is 416 × 416, centered at (204.5, 145) in widget screen
  coordinates. Its quaternion represents Unity Z = -44°, or screen Z = +44°.
  The screen sector therefore runs from 134° to 134° + 90° × throttle.
- The hidden `MSK-progress bar` is a Unity stencil Mask. Its alpha determines
  stencil coverage, not a second multiplicative alpha fade on the green image.
  KSI2 precomputes that coverage with the rotated radial texture, then clips
  its cached rectangles against the moving sector with floating-point polygons.
- Handle motion combines the full 290-unit BottomToTop slider travel with the
  eleven path offsets. Screen Y is `290 * (1 - throttle) - pathY`. The path
  quaternion angles unwrap from -140° to -220°, with the sprite child adding
  180°. Flipping Unity Y to screen Y negates the resulting angle. Node pivots
  become (0.5, 0.5) through the path's PosRot update mode.
- KSA's GLFW ImGui backend assigns window width/height to DisplaySize and
  window ContentScale to DisplayFramebufferScale. Draw-list coordinates are
  display coordinates; KSI2 does not apply framebuffer scaling a second time.
  KSA's separate ImGuiHelper.InterfaceScale comes from GameSettings and is
  used explicitly by native widgets; it is not automatically applied to these
  raw draw-list vertices.
- The adjacent navball prefab serializes a 1920 × 1080 reference and Expand
  mode. Decompiled CanvasScalerExtended implements Expand with
  `min(width/referenceWidth, height/referenceHeight)`. KSI2 uses this same
  formula in display coordinates with its own user multiplier. The throttle's
  outer runtime canvas has not been verified, so this is not a claim of exact
  physical-pixel equivalence to the complete KSP2 HUD.

## Rendering constraints

Custom ImGui texture uploads correlated with Vulkan device loss in user tests.
This build decodes PNGs once with Brutal's CPU decoder, releases each decoded
native image in a finally block, and caches colored rectangles. Adjacent equal
runs are merged vertically. Rendering uses ordinary ImGui filled primitives
and the existing font atlas. It allocates no custom Vulkan textures, submits
no custom queue work, and performs no file reads during steady rendering.

The static layers and handle preserve their source RGB and alpha, including
the dark handle interior.
The green packed ImGui color is ABGR `0xFF66FF00` (RGBA #00FF66). Per-rectangle
edge antialiasing is disabled to prevent seams between neighboring runs;
the original texture alpha supplies artwork edge coverage.

The source radial PNG has a small repeating alpha pattern. Drawing each texel
as translucent geometry exposes the blue bars beneath and causes the blue
speckles seen in the in-game photo. The compatibility renderer now treats
that alpha as a green shade over opaque fill coverage: brighter source cells
use the original `#00FF66`, darker cells remain green. It filters the pattern
over each logical sample. This reproduces the reference photo's green hatch
more closely but is an approximation of Unity texture filtering and blending.

The original `ELE-percent` is a `%` glyph in the `sbliquid-open1 SDF` TextMeshPro
font. The importer extracts Unicode 37 from that atlas for the lower gauge
symbol. The always-visible live number stays in KSA's font and now rotates
inside the handle. The handle itself is sampled from its original PNG in
display space, preserving its repeating grip-dot pattern as the marker turns.
The bottom glyph remains inside the source radial fill mask, so it appears
only when that part of the gauge is filled.

This CPU approach is a compatibility workaround. It has more geometry than
textured quads and does not reproduce Unity's bilinear filtering, sprite
padding/UV machinery, stencil edge sampling, or color-space conversion exactly.
These differences remain subject to an in-game visual comparison. The source
PNG canvases are now composed consistently, but pixel equivalence is unproven.

## Native GPU Texture Integration (Verified Path)

Rather than uploading raw Vulkan textures manually (which caused `VK_ERROR_DEVICE_LOST` during earlier experiments), KSA provides a native asset and texture registration pipeline:

1. **Asset Manifest Declaration (`mod.toml` & `KSI2Assets.xml`)**:
   - In `mod.toml`: `assets = ["KSI2Assets.xml"]`
   - In `KSI2Assets.xml`:
     ```xml
     <Assets>
       <Texture Id="KSI2ThrottleHandle" Path="[asset]" />
     </Assets>
     ```
   - KSA's mod loader loads the file reference prior to renderer initialization.

2. **ImGui Binding Lifecycle (`KSI2Mod.cs`)**:
   - During `[StarMapAllModsLoaded]`, retrieve the reference and mark it for ImGui before KSA binds renderer assets:
     ```csharp
     var textureRef = ModLibrary.Get<TextureReference>("KSI2ThrottleHandle");
     textureRef.AddToImgui();
     ```
   - When KSA runs `ModLibrary.Bind(_renderer)`, it creates the `SimpleVkTexture` using its own memory allocator and staging command buffer, transitions the image layout to `ShaderReadOnlyOptimal`, and registers it into the ImGui Vulkan backend (`ImGuiBackend.Vulkan.AddTexture(renderer.LinearSampler, ImageView)`).
   - This populates `textureRef.ImguiTextureRef`, which can be passed directly to ImGui draw calls without custom GPU submissions or synchronization hazards.

3. **ImGui Quad Rendering & UV Mapping Rules (`RasterArtwork.cs`)**:
   - ImGui provides `ImDrawList_AddImageQuad` for arbitrary rotating quads:
     ```csharp
     ImDrawList_AddImageQuad(target, tex_ref, &p1, &p2, &p3, &p4, &uv1, &uv2, &uv3, &uv4, col);
     ```
   - **Crucial Rule**: You MUST supply 4 distinct UV pointers matching the quad vertices in clockwise order:
     - `p1` (Top-Left): `Corner(-32.5f, -17f)` $\rightarrow$ `uv1 = new float2(0f, 0f)`
     - `p2` (Top-Right): `Corner(32.5f, -17f)` $\rightarrow$ `uv2 = new float2(1f, 0f)`
     - `p3` (Bottom-Right): `Corner(32.5f, 17f)` $\rightarrow$ `uv3 = new float2(1f, 1f)`
     - `p4` (Bottom-Left): `Corner(-32.5f, 17f)` $\rightarrow$ `uv4 = new float2(0f, 1f)`
   - **Pitfall Avoided**: Passing only 2 UV variables (`&uv1, &uv2, &uv2, &uv1`) transposes the vertical $V$ coordinate across the horizontal $X$ axis, causing the horizontal handle borders to draw as two vertical bars pointing upwards.
   - **Rotation Rule**: The handle's orientation on screen is fully defined by `pose.AngleDegrees` from `ThrottleGeometry.Handle(throttle)` plus the prefab's 180° child rotation (`-(pose.AngleDegrees + 180) * PI / 180`). Do NOT add manual $\pm 90^\circ$ offsets to the angle.
   - **Sprite Aspect Ratio**: The source PNG is 130×68 at 200 pixels-per-unit ($2\times$). Mapping UV $(0, 0)$ to $(1, 1)$ onto the 65×34 logical quad yields an exact 1:1 pixel mapping with the source art.


The movable bounds include the entire handle sweep and percentage column.
The default margin is 24 logical units; all elements share one layout scale.
The percentage intentionally uses KSA's font and stays visible. Edit mode
offers a display-only preview; it never sends throttle commands.

## Verification

`tools/Preview-Throttle.ps1` decodes local PNGs for an independent CPU test
process, checks real-artwork colors and bounds, and renders five positions
from the same cached geometry used by the mod. Its preview font is not KSA's
font. Generated previews remain in the ignored `artifacts/throttle-preview`.

Automated checks cover the eleven path nodes, the complete slider endpoints,
angle continuity, monotonic sector area, fill direction, clipping bounds,
handle bounds over a 1001-step sweep, telemetry rejection/clamping, percentage
formatting and malformed settings. They do not establish Vulkan stability or
in-game alignment. Those require another KSA test.

## Known Visual Follow-up

The percentage glyph is still slightly misaligned against the reference interface. Recheck its placement in-game and tune it in a later pass; current geometry checks do not establish final visual alignment.
