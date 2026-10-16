using Brutal.ImGuiApi;

namespace KSI2;

internal interface IPilotElement
{
    string Id { get; }
    void UpdateInput(PilotFrameLayout layout, PilotInteractionMode mode);
    void Draw(ImGuiViewportPtr viewport, PilotFrameLayout layout, PilotRenderContext context);
}

internal sealed class PilotElement : IPilotElement
{
    private readonly PilotUiLayer[] _components;

    internal PilotElement(string id, IEnumerable<PilotUiLayer> components)
    {
        Id = id;
        _components = components.OrderBy(component => PilotLayoutConfig.Current.Order(component.Order)).ToArray();
    }

    public string Id { get; }

    public void UpdateInput(PilotFrameLayout layout, PilotInteractionMode mode)
    {
        foreach (var component in _components)
            component.UpdateInput?.Invoke(layout, mode);
    }

    public void Draw(ImGuiViewportPtr viewport, PilotFrameLayout layout, PilotRenderContext context)
    {
        foreach (var component in _components)
        {
            try
            {
                if (PilotImGui.BeginArtworkWindow(viewport, $"{Id}-{component.Order}"))
                    component.Draw(layout, context);
            }
            finally
            {
                PilotImGui.EndArtworkWindow();
            }
        }
    }
}

internal sealed class PilotElementRenderer
{
    private readonly PilotUiSettings _settings;
    private readonly IPilotElement[] _elements;

    internal PilotElementRenderer(PilotUiSettings settings, IEnumerable<PilotUiLayer> components)
    {
        _settings = settings;
        var registered = components.GroupBy(component => component.ElementId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => (IPilotElement)new PilotElement(group.Key, group), StringComparer.Ordinal);
        if (registered.Count != PilotUiSettings.EditableElements.Length
            || PilotUiSettings.EditableElements.Any(id => !registered.ContainsKey(id)))
            throw new InvalidOperationException("Every editable pilot element must register draw components.");
        _elements = PilotUiSettings.EditableElements.Select(id => registered[id]).ToArray();
    }

    private IPilotElement[] InDrawOrder() => PilotElementOrder.Sort(_elements, element => element.Id,
        _settings);

    internal void UpdateInput(PilotFrameLayout layout, PilotInteractionMode mode)
    {
        foreach (var element in InDrawOrder()) element.UpdateInput(layout, mode);
    }

    internal void Draw(ImGuiViewportPtr viewport, PilotFrameLayout layout, PilotRenderContext context)
    {
        // OrderBy is stable. EditableElements defines the tie order for equal layers.
        foreach (var element in InDrawOrder()) element.Draw(viewport, layout, context);
    }
}
