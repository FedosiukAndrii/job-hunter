namespace JobHunter.Worker;

internal enum WorkerCommandKind
{
    Run,
    Migrate,
    Backup,
    Restore,
    IntegrityCheck,
    Help
}

internal sealed record WorkerCommand(
    WorkerCommandKind Kind,
    string[] ConfigurationArguments,
    string? InputPath = null,
    string? OutputPath = null)
{
    public static bool TryParse(string[] args, out WorkerCommand command)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length > 0 && args[0] == "--help")
        {
            command = new WorkerCommand(WorkerCommandKind.Help, args[1..]);
            return true;
        }

        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            command = new WorkerCommand(WorkerCommandKind.Run, args);
            return true;
        }

        var kind = args[0].ToLowerInvariant() switch
        {
            "run" => WorkerCommandKind.Run,
            "migrate" => WorkerCommandKind.Migrate,
            "backup" => WorkerCommandKind.Backup,
            "restore" => WorkerCommandKind.Restore,
            "integrity-check" => WorkerCommandKind.IntegrityCheck,
            "--help" or "-h" or "help" => WorkerCommandKind.Help,
            _ => (WorkerCommandKind?)null
        };

        if (kind is null)
        {
            command = null!;
            return false;
        }

        if (kind is WorkerCommandKind.Run or WorkerCommandKind.Migrate or WorkerCommandKind.Help)
        {
            command = new WorkerCommand(kind.Value, args[1..]);
            return true;
        }

        return TryParseMaintenanceCommand(kind.Value, args[1..], out command);
    }

    private static bool TryParseMaintenanceCommand(
        WorkerCommandKind kind,
        string[] args,
        out WorkerCommand command)
    {
        string? inputPath = null;
        string? outputPath = null;
        var configurationArguments = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] is "--input" or "--output")
            {
                if (index + 1 >= args.Length)
                {
                    command = null!;
                    return false;
                }

                if (args[index] == "--input")
                {
                    inputPath = args[++index];
                }
                else
                {
                    outputPath = args[++index];
                }

                continue;
            }

            configurationArguments.Add(args[index]);
        }

        var isValid = kind switch
        {
            WorkerCommandKind.Backup => inputPath is null && outputPath is not null,
            WorkerCommandKind.Restore => inputPath is not null && outputPath is not null,
            WorkerCommandKind.IntegrityCheck => outputPath is null,
            _ => false
        };

        if (!isValid)
        {
            command = null!;
            return false;
        }

        command = new WorkerCommand(
            kind,
            [.. configurationArguments],
            inputPath,
            outputPath);
        return true;
    }
}
