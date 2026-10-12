# Raster artwork at pilot UI scale

The bottom center icon comparison points to texture reduction as the main
reason its thin borders and glyph strokes fade. The evidence and its limits are
recorded in the sibling investigation repository's
`ui-raster-scaling-investigation.md`. In brief: both source icons are 48 × 48,
KSP2 gives them 24 × 24 UI rects, and the user saw sharp KSI2 icons at 48
screen pixels but saw the problem when one was reduced to 24 screen pixels.
The diagnostic comparison drew Object Cycle at 48 screen pixels and CommNet at
24, both white, with mipmaps enabled in its final build. The result of that
last mipmap-on comparison has not been reported.

## Recommended implementation path

1. Restore both status indicators to the authored 24 × 24 layout and placement
   after the comparison is complete. Keep the layout size distinct from the
   48 × 48 source texture size. Restore their state colors only after the
   sampling issue is resolved; white tint is useful for the current check.
2. For each small raster icon with 1 px source strokes, prepare a display-size
   version and inspect it at 1:1. At the 1920 × 1080 reference viewport and
   Scale 1, that means a 24 × 24 output for these icons. The present fix
   resolves each 2 × 2 source block to its strongest alpha, then draws the
   24 × 24 result as filled ImGui rectangles. This preserves coverage for
   these white, binary-alpha icons. Use alpha-aware area filtering for assets
   with gradients or partial transparency; a maximum-alpha rule can thicken
   strokes, bridge gaps, and change their intended appearance. Compare with
   KSP2, including the border and center glyph.
3. Render the prepared version at an integer number of screen pixels and
   align its bounds consistently. For other Scale values or viewport sizes,
   select or generate a version near the final pixel dimensions. Cache
   generated versions and rebuild them only when the effective size changes.
   Preserve the icon's center and its status/hit layout when rounding a
   destination rect.
4. Compare mipmaps enabled and disabled for each affected texture. Retain
   mipmaps where they help larger reductions or gradients; remove or replace
   them where they wash out fine detail. A mip setting alone did not fix the
   reported 24 px icon problem.
5. Check the result in game at the same viewport size and UI scale as the
   KSP2 reference, including fullscreen. Use screenshots and sampled pixels
   only as supporting evidence; inspect the visible shape and contrast too.

The KSA ImGui texture path uses a linear sampler. Drawing a prepared 24 px
result as 24 screen-aligned rectangles avoids another texture reduction at the
reference size. At arbitrary fractional scales, rasterization still has to
choose which screen pixels to cover; scale-specific preparation makes this
choice more deliberate. If an essential feature is narrower than one
destination pixel, no sampler can preserve all of its original shape and
brightness. It may need a larger drawn size, a thicker asset variant, or
geometry/vector drawing.

## Apply to the rest of the pilot interface

| Element | First check | Likely treatment |
|---|---|---|
| Small status, mode, and navball marker icons | Source pixels per displayed pixel; 1 px strokes; alpha edges | Prepare at the final size; keep state tint and status logic separate from sampling. |
| RCS/SAS outlines and other thin borders | Whether paired lines survive at the actual button size | Inspect at each supported scale; use a scale-specific asset or geometry if the lines cannot survive reduction. |
| LCD tiles, numerals, and other repeating pixels | Tile seams, mixed widths, and sampled glyph contrast | Check each tile and glyph at its destination size; preserve consistent tile boundaries when rounding. |
| Large dial artwork, gradients, and backgrounds | Line width, smooth gradients, clipping, and rotation | Keep mipmaps if they help; avoid forcing all large textures through the small-icon treatment. |
| Throttle CPU raster artwork | Its existing display-space sampling and run generation | Review separately; texture mipmap changes do not apply to these ImGui primitives. |
| Text rendered through ImGui fonts | Font size, atlas density, and glyph coverage | Diagnose through the font path rather than PNG icon resampling. |

Audit source dimensions, sprite rects, intended UI rects, effective screen
sizes, sampler/mipmap settings, tint, and alpha for each element. Changing only
one factor per comparison prevents color or placement changes from obscuring
the resampling result. The full UI's perceived darkness may have more than one
cause; only elements that show the same size-dependent loss should inherit
this treatment.

## Keep the Scale control

The Scale slider changes the intended size of the whole pilot interface and
should remain available. Removing it would only avoid some fractional sizes;
window size, fullscreen mode, display resolution, and KSP2's own UI scale can
also produce fractional mappings. KSI2 computes the effective layout scale as
`min(viewportWidth / 1920, viewportHeight / 1080) * savedScale`, then limits
it to fit the component in the viewport. Use that effective scale to choose
the final pixel size for each raster element. Keep positioning and interaction
bounds tied to layout coordinates; prepare the artwork for the resulting
screen size.

Expect tradeoffs at noninteger sizes: a 24-unit icon at effective scale 1.27
occupies about 30.48 screen pixels before rounding, so a 30 or 31 px output
may have slightly different spacing or apparent stroke weight. Rounding each
child independently can create gaps or overlap. Scaling all children from a
shared parent and rounding only deliberate raster boundaries helps preserve
their relationships. Also watch memory and rebuild cost if many variants are
cached, and avoid changing source alpha or state colors accidentally during
prefiltering.
