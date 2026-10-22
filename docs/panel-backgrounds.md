# Panel backgrounds

The configuration window's **Background style** selector offers Solid color,
Gaussian (single pass), and Gaussian (separable). Solid color exposes an RGB
picker with hex entry and a Reset button, saved as `SolidBackgroundColor`. Its default RGB is
`#030B19`; panel masks and individual artwork opacity are retained.
Each blur mode keeps its own opacity. Existing numeric mode values are retained;
the labels change without changing saved selections. Existing
`BlurPanelBackgrounds` settings migrate to Solid color or Gaussian (single pass).
The new process starts at 100% opacity, matching the reference material's use
of mask alpha. Both GPU modes use the existing five-second activation recovery;
switching between them rearms it. An interrupted activation recovers to Solid color.

## Shared drawing

`PanelBackgroundRenderer` owns method selection and solid fallback. Modules
receive it through `PilotRenderContext.Background` and provide bounds, a mask,
the fallback color, and any flip or nine-slice geometry. Captions use rectangular
masks. Modules retain borders, text, input and their existing drawing order.

Mask draws can select a UV rectangle, expressed as origin and size, in both
solid and blur modes. The time control uses the original rounded cap artwork
in one clipped region and the main fill in an adjacent region. Their shared
snapped boundary prevents double blending; the frame is clipped out of the
cap instead of being hidden by an opaque fill patch.

`PanelBackgroundBlur` draws masked panel quads in KSA's ImGui pass. Gaussian (single pass)
retains its 25-sample HDR filter, color transform and tint. The new method uses
the prepared scene texture and multiplies by the captured linear RGB vertex
tint `(4, 5, 7) / 255`. The KSP2 capture's panel vertex data contains
`04 05 07 FD`; its serialized sRGB tint was `24 26 2F FD`. Using the captured
bytes preserves Unity's 8-bit tint conversion and quantization.

## Experimental processed scene path

`PanelBlurRenderBridge` isolates the assumptions about KSA's private API. Its
optional Harmony postfix runs after `Cmaa2Renderer.RenderPreparePasses`, before
the presentation render pass. It accepts only the main viewport's renderer
instance and reads its enabled `_sceneLdrTarget`. The original hook on
`Program.RenderAaPreparePasses` could miss calls because that wrapper can be
inlined before Harmony patches it. The larger renderer method is the actual
recording point and has finished the LDR image's sampled-read transition.

`PanelBlurProcess` allocates resources during CPU frame preparation and records
into KSA's existing command buffer. It downsamples the processed LDR image to
half width and height, then performs horizontal, vertical, horizontal and vertical
passes with offset 2. Each pass uses the recovered five bilinear sample kernel:

| Sample | Offset in texels, multiplied by the axis offset | Weight |
| --- | --- | --- |
| Center | 0 | 0.227027 |
| Inner pair | +/-1.384615 | 0.316216 each |
| Outer pair | +/-3.230769 | 0.070270 each |

KSA's target helper requests storage-capable images. The implementation uses
`R8G8B8A8UNorm` targets and explicitly decodes sRGB **before** bilinear
interpolation, then encodes sRGB before every 8-bit write. This preserves the
reference's filtering order without requesting storage support for an sRGB
format. The final panel sample uses the same decode and interpolation rules.
The original LDR target is sampled directly; a full-resolution copy is omitted
because KSA leaves that source unchanged during these passes.

All panels share the prepared result. No offscreen passes or layout transitions
run inside ImGui callbacks. KSA's target helper performs the image barriers;
the process restores KSA's viewport and scissor after recording. Resize or source
view changes wait for the device before replacing resources and descriptors.
Mode changes retain allocations for reuse.

## Panel composition and graphics lifetime

The processed mode binds the original CMAA2 LDR image alongside the blurred
image and mask. Its shader computes the scene/blur mixture in linear RGB, then
encodes that result. Because KSA's UI target is UNorm, it supplies a corrected
premultiplied source to a `ONE, ONE_MINUS_SRC_ALPHA` RGB blend:

```text
P = linear tinted blur; D = encoded unblurred scene; a = mask alpha * opacity
sourceRGB = encode(a*P + (1-a)*decode(D)) - (1-a)*D
framebufferRGB = sourceRGB + (1-a)*existingFramebufferRGB
```

When the destination is the original scene, this reproduces linear sRGB-target
blending without changing KSA's UI render pass. Existing UI underneath remains
visible according to opacity and draw order. That overlapping UI contribution
still blends in encoded space; exact linear blending against arbitrary prior UI
would require access to its actual framebuffer color or a different UI target.
The correction does not alter fully opaque panel RGB, the captured tint, the
Gaussian kernel, or KSA's camera processing.

Alpha uses the captured KSP2 factors: `SRC_ALPHA, ONE_MINUS_SRC_ALPHA`, producing
`a*a + destinationAlpha*(1-a)`. Solid fallback under the processed pipeline
premultiplies its RGB so the fallback keeps normal source-over behavior. Current
blur retains its original blend pipeline.

`KsaGraphicsLifetime` installs notifications before KSA's `Program.Dispose` and
the Vulkan device's disposal. Registered graphics resources release in reverse
creation order at program-disposal entry, while the device and ImGui backend
are still alive. Subsequent StarMap unload is idempotent. The navball's custom
sampler participates because its previous shutdown path also waited on the
host's device. If the device is lost, the helper skips further native calls and
leaves device-owned allocations for host teardown; it still releases CPU callback
payloads. This protects our cleanup path and does not recover a lost GPU device.

If preparation fails, the panel callback binds a valid HDR descriptor and draws
the solid fill. It never binds an unwritten blur target for that fallback.
Unavailable CMAA2 or display formats use solid artwork directly, with a status
message in settings. If three requested frames pass without a result, the status
distinguishes a hook that was never called, a main view that was not identified,
and a pass that did not complete. The selected mode remains visible.

## Verification and remaining limits

The implementation builds against the installed KSA assemblies. The shaders
compile with KSA's `shaderc_shared` library. Focused checks cover migration,
independent opacity, recovery when switching modes, boxed command-buffer
forwarding, graphics-cleanup ordering, and hook removal. Setting
`KSA_REFERENCE_PATH` while running the checks additionally verifies that Harmony
accepts the installed KSA renderer and disposal methods:

```powershell
dotnet run --project tests/KSI2.GeometryTests --configuration Release -- --background-only
```

These checks do not execute GPU rendering. In-game validation still needs to
cover all three styles, changing opacity, resizing, secondary viewports and
CMAA2 toggling. The first successful recording logs source/output dimensions
and the pass sequence. A GPU capture is needed to confirm synchronization and
the intermediate pixel values; a matching screenshot alone cannot prove that.

All four shaders compile with the installed shaderc library. A local numerical
audit of the new scene-edge equation covered 3,145,728 channel/input/alpha cases;
its maximum error against double-precision linear blending was below 0.00004
of one 8-bit level. This checks the equations, not execution on the GPU.

This reproduces the measured blur stages over KSA's displayed scene. KSA's
scene, tone mapping and anti-aliasing differ from KSP2. Partially transparent
panels over previously drawn UI retain the encoded-space overlap limitation
described above. The intermediate targets remain UNorm with explicit sRGB
conversion; native sRGB allocation would require replacing the storage-capable
target helper and is not needed for the scene-edge correction.
