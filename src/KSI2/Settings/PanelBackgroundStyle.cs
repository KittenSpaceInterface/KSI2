namespace KSI2;

internal enum PanelBackgroundStyle { Solid, CurrentBlur, Ksp2Blur }

internal static class PanelBackgroundStyles
{
    internal static string Label(PanelBackgroundStyle style) => style switch
    {
        PanelBackgroundStyle.CurrentBlur => "Gaussian (single pass)",
        PanelBackgroundStyle.Ksp2Blur => "Gaussian (separable)",
        _ => "Solid color"
    };
}
