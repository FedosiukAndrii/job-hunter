namespace JobHunter.Worker;

internal enum WorkerCommandKind
{
    Run,
    Migrate,
    Help
}

internal readonly record struct WorkerCommand(
    WorkerCommandKind Kind,
    string[] ConfigurationArguments)
{
    public static bool TryParse(string[] args, out WorkerCommand command)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            command = new WorkerCommand(WorkerCommandKind.Run, args);
            return true;
        }

        var kind = args[0].ToLowerInvariant() switch
        {
            "run" => WorkerCommandKind.Run,
            "migrate" => WorkerCommandKind.Migrate,
            "--help" or "-h" or "help" => WorkerCommandKind.Help,
            _ => (WorkerCommandKind?)null
        };

        if (kind is null)
        {
            command = default;
            return false;
        }

        command = new WorkerCommand(kind.Value, args[1..]);
        return true;
    }
}
