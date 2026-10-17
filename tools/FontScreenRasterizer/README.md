# Temporary font rendering screen

The Configuration window opens a temporary diagnostic screen at native framebuffer resolution. Every mode has the same opaque background and samples the unchanged registered Open font at 7, 10, 14, 18, 21.6, 24, 32, and 48 pixels. KSI2 displays each plate without downscaling; it does not alter the font file.

Regenerate all four PNG plates with the registered Open OTF as the first argument:

```powershell
dotnet run --project tools/FontScreenRasterizer -- "<path-to-registered-open-otf>" "src/KSI2/assets/font-tests/segment-open-sizes.png"
```

The base plate uses WPF's glyph rasterizer. Three additional plates use DirectWrite's bitmap render target in aliased, grayscale, and ClearType modes. The DirectWrite samples use the same OTF glyph indices and advances at 96 DPI. Size labels on those plates use GDI and are only row markers. Two additional in-game modes render the Open OTF and KSA's UI font live through ImGui. These are comparisons, not the final HUD rendering path.

In game, use **KSI2 Configuration → Font Rendering Test (temporary)** and change **Rendering mode**. Inspect at the game's native display scale; operating-system screenshot zoom can hide individual pixels. The bitmap modes are pre-rendered from the OTF; the ImGui modes draw live each frame.
