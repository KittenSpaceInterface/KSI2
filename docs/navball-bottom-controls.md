# Navball bottom controls

`NavballBottomControls` draws the four items at the bottom of KSP2's navball
cluster. Its coordinates, state colors, and roles come from the investigation
in `interface-investigation/navball-bottom-icons-investigation.md`.

- RCS and SAS are clickable. They call KSA's `FlightComputer.ToggleRCSMode()`
  and `Vehicle.ToggleStabilization()` and read the resulting game state each
  frame. The outline includes the dots above and below the circle.
- The object-cycle glyph is a status indicator, bright blue when more than one
  live vessel is in KSA's current frame and dim blue otherwise. KSA's cycle
  command remains on its existing input binding.
- KSA does not expose KSP2 CommNet status. The signal glyph is green when the
  controlled KSA vehicle is controllable and dim blue otherwise. It reports
  local vehicle control, not a simulated radio link.

SAS continues to use the existing `LCD_Solid.ttf` face through KSA's ImGui font
manager. RCS uses the renderer selected in the temporary Font Rendering Test
screen. DirectWrite and GDI rasterize the selected font at the requested
physical size. GDI loads the font privately from its file into a memory bitmap;
it does not use DirectWrite's GDI rendering modes. The SDF atlas renderer
samples R, C, and S distance fields for the selected font. All three paths
cache coverage and compose it through ImGui rectangles. RCS is centered on
physical framebuffer pixels. The existing speed and altitude PNG font atlases
for numeric values are unchanged; the speed card labels and units use the
selected font test renderer.

The KSI2 Configuration window opens the temporary Font Rendering Test screen.
Its preset selector includes a KSP2 approximation, the earlier SDF settings,
and GDI grayscale. The renderer selector chooses DirectWrite, SDF atlas, or
GDI. The font selector is available in every renderer and chooses reconstructed
Open, reconstructed Solid, or the downloaded demo Open TTF. DirectWrite exposes
rendering mode, antialiasing, measuring mode, grid fitting, subpixel geometry,
gamma, contrast, ClearType level, and glyph analysis. SDF atlas exposes its
display gradient and RCS size. GDI exposes grayscale, ClearType, or aliased
quality and RCS size. The SDF atlases contain the extracted character sets for
reconstructed Open and Solid plus the demo font's supported glyphs; the atlas
renderer looks up characters by Unicode codepoint. Controls update both
the screen and the RCS button immediately and reset on restart. The KSP2
approximation preset uses unhinted grayscale DirectWrite at 18 pixels; KSP2
itself uses TextMeshPro SDF. The test screen shows RGB coverage on its solid
background. RCS combines coverage into one tintable alpha mask so its animated
color can overlay the game view.

The SDF controls also have a temporary linear-light coverage correction toggle.
It is on by default for the SDF preset and affects both the sample and RCS.
It applies an sRGB transfer curve to the SDF coverage before KSA's ImGui alpha
blend. A separate faint-pixel suppression control fades the lowest coverage
values before that transfer, since the curve otherwise makes them visible as
ghost pixels. The SDF edge selector offers an orthographic TextMesh Pro scale
estimate with a linear edge, a linear edge at the earlier scale, and the earlier
smoothstep edge. The rest-color preview shows both off and on text over a dark
background. These controls affect the RCS button for all three font choices.
The material's gradient value remains 6 in the SDF preset. The reconstructed
font data and authored label colors are unchanged. Neither the blend correction
nor the scale estimate establishes the exact KSP2 runtime shader behavior.

The existing LCD font, reconstructed Solid test font, downloaded demo Open TTF,
and the three full-character SDF atlases are packaged under `assets/fonts/`. The
reconstructed Open test font is resolved beside a registered navball asset.
