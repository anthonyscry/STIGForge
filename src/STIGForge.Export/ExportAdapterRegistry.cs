namespace STIGForge.Export;

public sealed class ExportAdapterRegistry
{
    private readonly List<IExportAdapter> _adapters = new();

    public void Register(IExportAdapter adapter)
    {
        if (adapter == null) throw new ArgumentNullException(nameof(adapter));
        _adapters.Add(adapter);
    }

    public IExportAdapter? TryResolve(string formatName)
    {
        return _adapters.FirstOrDefault(a =>
            string.Equals(a.FormatName, formatName, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<IExportAdapter> GetAll() => _adapters.AsReadOnly();
}
