# Navball lettering and shadow

KSP2 renders the navball mesh into a texture at twice the RawImage resolution,
then displays that texture at roughly 176 pixels of visible sphere diameter.
KSI2 now follows the same spatial sampling step in `NavballSupersampler`: it
rasterizes the original mesh and surface texture at twice the displayed ball
diameter, averages each 2 × 2 group of samples, and submits the resolved pixels
to ImGui. This preserves the ball's displayed size and is intended to improve
coverage of thin numbers and markings near its edge.

The intermediate is built on the CPU because KSI2's `StarMapAfterGui` hook
submits draw commands to an ImGui pass that KSA has already begun. It is not a
Unity `RenderTexture` or a Vulkan offscreen pass. The rasterizer uses the source
texture's base level with bilinear sampling, like the single-mip KSP2 source
texture. If the source pixels are unavailable, KSI2 falls back to its previous
direct mesh draw with `NavballTextureFiltering`.

The locally extracted `Flight-Navball-Shadow` remains a single full-opacity
copy over the ball. KSP2's prefab contains multiple shadow Images, but the
single copy is KSI2's existing readability adjustment.

The Release build succeeds, and an in-game check showed a considerable
readability improvement. Frame cost has not been measured.
KSP2's GPU color-space handling and its exact 390 × 390 render target remain
differences from this software implementation.
