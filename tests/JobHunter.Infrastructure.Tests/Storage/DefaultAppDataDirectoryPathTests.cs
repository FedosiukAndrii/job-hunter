using JobHunter.Infrastructure.Storage;

namespace JobHunter.Infrastructure.Tests.Storage;

public sealed class DefaultAppDataDirectoryPathTests
{
    [Fact]
    public void ResolveUsesLocalAppDataOnWindows()
    {
        var inputs = new PlatformPathInputs(
            HostOperatingSystem.Windows,
            @"C:\Users\candidate\AppData\Local",
            @"C:\Users\candidate",
            null);

        var result = DefaultAppDataDirectoryPath.Resolve(inputs);

        Assert.Equal(@"C:\Users\candidate\AppData\Local\JobHunter", result);
    }

    [Fact]
    public void ResolveUsesApplicationSupportOnMacOS()
    {
        var inputs = new PlatformPathInputs(
            HostOperatingSystem.MacOS,
            null,
            "/Users/candidate",
            null);

        var result = DefaultAppDataDirectoryPath.Resolve(inputs);

        Assert.Equal("/Users/candidate/Library/Application Support/JobHunter", result);
    }

    [Fact]
    public void ResolvePrefersXdgDataHomeOnLinux()
    {
        var inputs = new PlatformPathInputs(
            HostOperatingSystem.Linux,
            null,
            "/home/candidate",
            "/var/lib/candidate");

        var result = DefaultAppDataDirectoryPath.Resolve(inputs);

        Assert.Equal("/var/lib/candidate/job-hunter", result);
    }

    [Fact]
    public void ResolveFallsBackToUserLocalShareOnLinux()
    {
        var inputs = new PlatformPathInputs(
            HostOperatingSystem.Linux,
            null,
            "/home/candidate",
            null);

        var result = DefaultAppDataDirectoryPath.Resolve(inputs);

        Assert.Equal("/home/candidate/.local/share/job-hunter", result);
    }

    [Fact]
    public void ResolveRejectsRelativeXdgDataHome()
    {
        var inputs = new PlatformPathInputs(
            HostOperatingSystem.Linux,
            null,
            "/home/candidate",
            "relative/path");

        var exception = Assert.Throws<InvalidOperationException>(
            () => DefaultAppDataDirectoryPath.Resolve(inputs));

        Assert.Contains("XDG_DATA_HOME", exception.Message, StringComparison.Ordinal);
    }
}
