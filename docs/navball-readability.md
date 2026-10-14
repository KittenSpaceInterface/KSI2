# Navball lettering and shadow

KSP2 renders the navball mesh into a 390 × 390 texture at unit UI scale, then
displays it in a 195 × 195 RawImage. Its sphere is about 177 pixels across,
with transparent margin inside the RawImage. KSI2 follows these dimensions in
`NavballSupersampler`: it rasterizes the mesh into a 390 × 390 CPU buffer,
averages each 2 × 2 group of samples, and submits the 195 × 195 result to
ImGui. The result's origin and size are aligned to framebuffer pixels, and
the sphere, shadow, markers, and crosshair share the aligned center. At other
UI scales, both dimensions follow the scaled, pixel-aligned RawImage size.

The intermediate is built on the CPU because KSI2's `StarMapAfterGui` hook
submits draw commands to an ImGui pass that KSA has already begun. It is not a
Unity `RenderTexture` or a Vulkan offscreen pass. The rasterizer uses the source
texture's base level with bilinear sampling, like the single-mip KSP2 source
texture. If the source pixels are unavailable, KSI2 falls back to its previous
direct mesh draw with `NavballTextureFiltering`.

For color-aware filtering, the CPU rasterizer decodes the source RGB
from sRGB before bilinear sampling, averages the high-resolution samples in
linear light, then encodes the resolved RGB for ImGui. This keeps solid source
colors unchanged and can brighten partially covered white markings. The user
reported an in-game improvement. KSP2's runtime color-space and render-target
handling have not been measured.

The earlier twofold sphere render improved readability in an in-game check.
Replacing the reflected KSP2-to-KSA mapping with a quarter turn placed 90 to
the left of North and aligned the sphere with the prograde and radial markers.
The user reported a large further quality improvement with that rotation and
color-aware filtering installed. Their separate contributions were not
measured. Frame cost and KSP2's GPU color handling remain unmeasured.
