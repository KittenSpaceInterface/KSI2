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

KSA's asset registration binds the source layers, handle, percentage glyph,
and derived green fill to ImGui with its linear texture sampler. The mod keeps
CPU decoded artwork as a fallback if native registration is unavailable. It
submits no custom Vulkan work and reads no files during steady rendering.

The importer builds `[asset]` at 170 x 580 from the source bar
mask and radial texture. It preserves fractional alpha at the bar edges and
uses bilinear radial samples for the green shade. The renderer maps this image
to the 85 x 290 widget and clips its triangles to the moving radial sector.
The native sampler blends source texels at the displayed size, avoiding the
seams and pixel stair steps of thousands of CPU rectangles.

The source radial PNG contains a repeating alpha lattice. The derived fill
uses it as a green shade over coverage: brighter cells use `#00FF66`, darker
cells remain green. This approximates the KSP2 mask and blend behavior.

The original `ELE-percent` is a TextMeshPro object containing `%`, using the
`sbliquid-open1 SDF` font. Its RectTransform is bottom-right anchored at
`(-12, 16)`, size `25x25`, pivot `(1, 0)`, under `BG-Throttle-Ticks`. Its widget
bounds are `(48, 249)` to `(73, 274)`, center `(60.5, 261.5)`. The importer
extracts Unicode 37 from the source atlas. The glyph displays at 90% of the
atlas-derived `13.22x18.07` logical size (`11.898x16.263`). This is an
in-game visual adjustment rather than a serialized TMP size. Its SDF coverage ramp is widened
according to the material gradient scale and the reduction to display size.
The appearance remains an approximation of the TMP shader.

In the source prefab, the live number is TextMeshProUGUI using
`realtimerounded-regular SDF` (family Realtime Rounded, font size 22). It is a
different font from the bottom `%` glyph's `sbliquid-open1 SDF`. KSI2 currently
draws the live number with KSA's active ImGui font, so its typeface does not
yet match the source prefab. The bottom glyph appears only when the lower
gauge is filled. Pixel equivalence still needs an in-game comparison because
Unity masking and color-space conversion differ.

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

Compare the new filtered fill and percentage glyph against KSP2 in-game. The
geometry checks do not establish final visual equivalence.

The source handle label uses Realtime Rounded SDF. KSI2 can use an optional
locally generated atlas from a user-provided OTF; when that atlas is absent,
it uses the active ImGui font. KSA's `Content/Fonts` folder currently contains
`3270-Regular.ttf` and `MilSpec33558.ttf`, neither matching that source font.
The handle atlas contains the values 0 through 100 rendered from the local OTF.
Using a complete font for general KSI2 text would require a redistributable
font file and a safe way to add it to KSA's ImGui font atlas.
