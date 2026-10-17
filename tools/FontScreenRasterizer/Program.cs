using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: FontScreenRasterizer <registered-font-file> <output-png>");
    return 1;
}

var fontPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
var face = new GlyphTypeface(new Uri(fontPath));
const int width = 980;
const int height = 580;
var sizes = new[] { 7d, 10d, 14d, 18d, 21.6d, 24d, 32d, 48d };
const string sample = "RCS 0123456789";
var background = new SolidColorBrush(Color.FromRgb(25, 35, 45));
var ink = new SolidColorBrush(Color.FromRgb(204, 178, 170));
var labelInk = new SolidColorBrush(Color.FromRgb(132, 151, 161));
background.Freeze();
ink.Freeze();
labelInk.Freeze();

var visual = new DrawingVisual();
RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);
TextOptions.SetTextRenderingMode(visual, TextRenderingMode.ClearType);
TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
using (var draw = visual.RenderOpen())
{
    draw.DrawRectangle(background, null, new Rect(0, 0, width, height));
    var y = 18d;
    foreach (var size in sizes)
    {
        var rowHeight = Math.Max(34d, size + 19d);
        var label = new FormattedText($"{size:0.#} px", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13d, labelInk, 1d);
        draw.DrawText(label, new Point(24, y + (rowHeight - label.Height) / 2));

        var glyphs = new List<ushort>();
        var advances = new List<double>();
        foreach (var character in sample)
        {
            if (!face.CharacterToGlyphMap.TryGetValue(character, out var glyph))
                throw new InvalidDataException($"Font lacks U+{(int)character:X4}.");
            glyphs.Add(glyph);
            advances.Add(face.AdvanceWidths[glyph] * size);
        }

        var baseline = y + (rowHeight - size) / 2 + face.Baseline * size;
        var run = new GlyphRun(face, 0, false, size, 1f, glyphs,
            new Point(160, baseline), advances, null,
            sample.ToCharArray(), null, null, null, null);
        draw.DrawGlyphRun(ink, run);
        y += rowHeight + 10d;
    }
}

var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
bitmap.Render(visual);
var encoder = new PngBitmapEncoder();
encoder.Frames.Add(BitmapFrame.Create(bitmap));
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
using (var stream = File.Create(outputPath)) encoder.Save(stream);
Console.WriteLine($"Rendered {Path.GetFileName(outputPath)} from {Path.GetFileName(fontPath)} ({width}x{height}, 96 DPI).");
DirectWriteRasterizer.RenderModes(fontPath, outputPath, face, sizes, sample, width, height);
return 0;
