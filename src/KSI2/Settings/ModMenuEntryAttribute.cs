namespace KSI2;

// ModMenu discovers this attribute by name, so its assembly is not a dependency.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class ModMenuEntryAttribute(string menuName, string? isModMenuActivePropertyName = null)
    : Attribute
{
    public string MenuName { get; } = menuName;
    public string? IsModMenuActivePropertyName { get; } = isModMenuActivePropertyName;
}
