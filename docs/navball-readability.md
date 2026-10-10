# Navball lettering and shadow

The ball projects a 1024-pixel texture into a roughly 176-pixel diameter at
1920 x 1080. Sampling just the base level with a bilinear UI sampler can skip
thin parts of numbers as the ball rotates. The surface manifest now enables
mip generation. `NavballTextureFiltering` registers a separate ImGui descriptor
for the same game-managed image with trilinear filtering and up to 8x anisotropy,
capped by the device limit. Anisotropy preserves detail along the narrower axis
of the texture footprint where the sphere curves away from the viewer.

Local inspection of `TextureReference.Bind`, `Presets.Sampler.SamplerLinearClamped`,
and `KSADeviceContextEx` confirms that the default UI sampler supports mip levels
but disables anisotropy, and KSA enables sampler anisotropy on its device. No
texture upload or render-target synchronization is replaced. The custom sampler
and descriptor are released after waiting for submitted frames at mod unload;
creation failure falls back to the original UI descriptor.

The shadow now draws one full-opacity copy of the original gradient. Previously
three full-opacity copies plus one 40% copy compounded its alpha: a texel with
50% alpha obscured 90% of the ball (`1 - 0.5^3 * 0.8`). One copy obscures 50%.
This is an intentional KSI2 readability adjustment, not a revision to the KSP2
prefab findings. The earlier single-copy 40% trial documented in the investigation
was too faint; this version retains the full gradient and rim.

The KSP2 research is in the sibling investigation repository's
`navball-followup-investigation.md`. KSP2's actual twofold offscreen render and
resolve remains a different pipeline; mip/anisotropic filtering does not
reproduce that resolve or add silhouette antialiasing.

Validation: Release build and existing geometry checks pass. Runtime visual
acceptance is still required: compare the same attitude at the same viewport
size, inspect center and rim numbers, and slowly roll to check lettering
stability. Check the startup log for the navball filtering message. No source
artwork is modified or added to version control.
