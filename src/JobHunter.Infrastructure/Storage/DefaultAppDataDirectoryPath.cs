namespace JobHunter.Infrastructure.Storage;

internal static class DefaultAppDataDirectoryPath
{
    public static string Resolve(PlatformPathInputs inputs) =>
        inputs.OperatingSystem switch
        {
            HostOperatingSystem.Windows =>
                CombineWindows(RequirePath(inputs.LocalApplicationData, "LOCALAPPDATA"), "JobHunter"),
            HostOperatingSystem.MacOS =>
                CombineUnix(RequirePath(inputs.UserProfile, "the user profile"), "Library/Application Support/JobHunter"),
            HostOperatingSystem.Linux when !string.IsNullOrWhiteSpace(inputs.XdgDataHome) =>
                CombineUnix(RequireUnixAbsolutePath(inputs.XdgDataHome, "XDG_DATA_HOME"), "job-hunter"),
            HostOperatingSystem.Linux =>
                CombineUnix(RequirePath(inputs.UserProfile, "the user profile"), ".local/share/job-hunter"),
            _ => throw new PlatformNotSupportedException(
                "Job Hunter supports Windows, macOS, and Linux.")
        };

    private static string CombineWindows(string root, string relativePath) =>
        $"{root.TrimEnd('\\', '/')}" + $@"\{relativePath}";

    private static string CombineUnix(string root, string relativePath) =>
        $"{root.TrimEnd('/')}/{relativePath}";

    private static string RequirePath(string? path, string source)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"Unable to resolve {source} for the application data directory.");
        }

        return path;
    }

    private static string RequireUnixAbsolutePath(string? path, string source)
    {
        var requiredPath = RequirePath(path, source);
        if (requiredPath[0] != '/')
        {
            throw new InvalidOperationException($"{source} must contain an absolute path.");
        }

        return requiredPath;
    }
}
