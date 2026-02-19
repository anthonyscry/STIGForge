namespace STIGForge.Export;

public sealed class ExportOrchestrator
{
    private readonly ExportAdapterRegistry _registry;

    public ExportOrchestrator(ExportAdapterRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<ExportAdapterResult> ExportAsync(
        string formatName,
        ExportAdapterRequest request,
        CancellationToken ct)
    {
        var adapter = _registry.TryResolve(formatName);
        if (adapter == null)
        {
            return new ExportAdapterResult
            {
                Success = false,
                ErrorMessage = $"No export adapter registered for format '{formatName}'."
            };
        }

        return await adapter.ExportAsync(request, ct).ConfigureAwait(false);
    }
}
