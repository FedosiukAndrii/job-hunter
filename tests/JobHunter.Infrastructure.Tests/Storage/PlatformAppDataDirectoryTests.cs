using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Tests.Storage;

public sealed class PlatformAppDataDirectoryTests
{
    [Fact]
    public void ConstructorCreatesConfiguredAbsoluteDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory(create: false);

        var result = new PlatformAppDataDirectory(
            Options.Create(new StorageOptions { DataDirectory = temporaryDirectory.Path }));

        Assert.Equal(temporaryDirectory.Path, result.RootPath);
        Assert.True(Directory.Exists(temporaryDirectory.Path));
    }

    [Fact]
    public void GetPathRejectsTraversalOutsideRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var appDataDirectory = new PlatformAppDataDirectory(
            Options.Create(new StorageOptions { DataDirectory = temporaryDirectory.Path }));

        Assert.Throws<ArgumentException>(
            () => appDataDirectory.GetPath(Path.Combine("..", "outside.db")));
    }
}
