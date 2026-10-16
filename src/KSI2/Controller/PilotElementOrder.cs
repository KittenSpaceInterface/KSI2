namespace KSI2;

internal static class PilotElementOrder
{
    // OrderBy preserves registration order when elements use the same layer.
    internal static T[] Sort<T>(IEnumerable<T> elements, Func<T, string> id,
        PilotUiSettings settings) => elements.OrderBy(element => settings.ElementLayer(id(element))).ToArray();
}
