using JobHunter.Application.Storage;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Storage;

public sealed class PlatformAppDataDirectory : IAppDataDirectory
{
    public PlatformAppDataDirectory(IOptions<StorageOptions> options)
        : this(options, PlatformPathInputs.Create())
    {
    }

    internal PlatformAppDataDirectory(
        IOptions<StorageOptions> options,
        PlatformPathInputs platformPathInputs)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuredPath = options.Value.DataDirectory?.Trim();
        var selectedPath = configuredPath ?? DefaultAppDataDirectoryPath.Resolve(platformPathInputs);

        if (!Path.IsPathFullyQualified(selectedPath))
        {
            throw new InvalidOperationException("The application data directory did not resolve to an absolute path.");
        }

        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedPath));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string GetPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new ArgumentException("The path must be relative to the application data directory.", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        var pathFromRoot = Path.GetRelativePath(RootPath, fullPath);

        if (pathFromRoot.Equals("..", StringComparison.Ordinal)
            || pathFromRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(pathFromRoot))
        {
            throw new ArgumentException("The path must remain inside the application data directory.", nameof(relativePath));
        }

        return fullPath;
    }
}
