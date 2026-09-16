namespace JobHunter.AI.Abstractions;

public sealed class JobAnalyzerSelector
{
    private readonly Dictionary<string, IJobAnalyzer> _analyzers;
    private readonly NullJobAnalyzer _disabledAnalyzer;

    public JobAnalyzerSelector(
        IEnumerable<IJobAnalyzer> analyzers,
        NullJobAnalyzer disabledAnalyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzers);
        ArgumentNullException.ThrowIfNull(disabledAnalyzer);

        _disabledAnalyzer = disabledAnalyzer;
        _analyzers = analyzers
            .Where(analyzer => analyzer is not NullJobAnalyzer)
            .ToDictionary(
                analyzer => analyzer.Capabilities.Provider,
                StringComparer.OrdinalIgnoreCase);
    }

    public IJobAnalyzer Select(bool enabled, string provider)
    {
        if (!enabled)
        {
            return _disabledAnalyzer;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        return _analyzers.TryGetValue(provider.Trim(), out var analyzer)
            ? analyzer
            : throw new InvalidOperationException(
                $"AI provider '{provider.Trim()}' is not registered.");
    }
}
