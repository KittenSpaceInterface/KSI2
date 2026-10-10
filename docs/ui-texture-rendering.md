# UI texture rendering

The throttle fill exposed a rendering issue that can affect other small UI
artwork. KSP2 stores its 170 x 580 bar mask and 832 x 832 radial texture with
`m_FilterMode: 1` (bilinear). The old KSI2 path converted their pixels into
thousands of small ImGui rectangles inside an 85 x 290 widget. Each rectangle
kept a single sampled color, so adjacent source pixels formed visible steps
when the art was reduced. Disabling polygon antialiasing avoided seams between
rectangles but did not reconstruct the filtered source image.

For future UI elements:

1. Record the source texture dimensions, filter mode, sprite rectangle, UVs,
   and intended display size during interface investigation. Check each asset;
   the throttle's filter setting is not proof that every KSP2 texture uses it.
2. Render small or reduced raster artwork through KSA's registered texture
   path and `ImguiTextureRef` so its linear sampler blends texels at display
   size. Keep UVs consistent across a clipped or rotated shape.
3. If several source masks must be combined, generate a derived texture at
   source resolution, preserve fractional edge alpha, then let the sampler
   reduce it. The throttle importer does this for `[asset]`.
4. Treat signed distance font atlases separately. Their alpha represents
   distance, so a fixed binary threshold or narrow coverage ramp can make
   small glyphs look thick and jagged. Use the source font and material data
   when possible; otherwise record the raster conversion and its limits.

The throttle fill and percent glyph remain visual approximations of Unity's
masking and TextMeshPro shader. Compare them in game before reusing their
specific color or SDF conversion constants elsewhere.

## Future work: blurred panel backgrounds

KSP2 uses `UIBlur2` on the atmosphere fill, vertical-speed panel, throttle
background, and navball cluster background. KSI2 currently draws their original
shape masks with a flat color; `#030B19` is the current temporary visual trial
for all four. Recreate the scene sampling and blur when KSA provides a suitable
render stage, and audit other HUD panels for the same material. The shader, blur path,
and KSA integration constraints are documented in
`interface-investigation/blur-panel-rendering-investigation.md`.
