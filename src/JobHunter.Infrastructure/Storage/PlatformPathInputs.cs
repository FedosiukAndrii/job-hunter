namespace JobHunter.Infrastructure.Storage;

internal enum HostOperatingSystem
{
    Windows,
    MacOS,
    Linux
}

internal readonly record struct PlatformPathInputs(
    HostOperatingSystem OperatingSystem,
    string? LocalApplicationData,
    string? UserProfile,
    string? XdgDataHome)
{
    public static PlatformPathInputs Create()
    {
        var operatingSystem = global::System.OperatingSystem.IsWindows()
            ? HostOperatingSystem.Windows
            : global::System.OperatingSystem.IsMacOS()
                ? HostOperatingSystem.MacOS
                : global::System.OperatingSystem.IsLinux()
                    ? HostOperatingSystem.Linux
                    : throw new PlatformNotSupportedException(
                        "Job Hunter supports Windows, macOS, and Linux.");

        return new PlatformPathInputs(
            operatingSystem,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
    }
}
