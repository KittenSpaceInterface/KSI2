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

The locally extracted `Flight-Navball-Shadow` remains a single full-opacity
copy over the ball. KSP2's prefab contains multiple shadow Images, but the
single copy is KSI2's existing readability adjustment.

The earlier twofold sphere render improved readability in an in-game check.
The exact RawImage dimensions and pixel alignment have not yet had an in-game
visual comparison. Frame cost has not been measured. KSP2's GPU texture
filtering and color-space handling remain different from this CPU pass.
