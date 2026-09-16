using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.Application.Profiles;
using JobHunter.Application.Storage;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JobHunter.Infrastructure.Profiles;

public sealed class FileCandidateProfileLoader(
    IAppDataDirectory appDataDirectory,
    IOptions<ProfileOptions> options)
    : ICandidateProfileLoader
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<LoadedCandidateProfile> LoadAsync(CancellationToken cancellationToken)
    {
        var profilePath = DiscoverProfilePath();
        var profileText = await ReadBoundedUtf8Async(
            profilePath,
            options.Value.MaximumProfileBytes,
            "$",
            "Reduce the structured profile size.",
            cancellationToken);
        var profile = DeserializeProfile(profilePath, profileText);
        CandidateProfileValidator.Validate(profile);

        var cvPath = ResolveCvPath(profilePath, profile.SupplementalCvPath);
        var redactedCv = cvPath is null
            ? null
            : SensitiveTextRedactor.Redact(
                await ReadBoundedUtf8Async(
                    cvPath,
                    options.Value.MaximumCvBytes,
                    "$.supplementalCvPath",
                    "Reduce the Markdown CV size or increase Profile:MaximumCvBytes within its limit.",
                    cancellationToken));
        var canonicalJson = JsonSerializer.Serialize(profile, JsonOptions);
        var hashInput = $"{canonicalJson}\n{redactedCv}";
        var contentHash =
            $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))}";

        return new LoadedCandidateProfile(
            profile,
            canonicalJson,
            contentHash,
            profilePath,
            redactedCv);
    }

    private string DiscoverProfilePath()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.FilePath))
        {
            var configuredPath = Path.GetFullPath(options.Value.FilePath);
            if (!File.Exists(configuredPath))
            {
                throw ProfileError(
                    "$",
                    $"Configured profile file '{configuredPath}' does not exist.",
                    "Create the file or update Profile:FilePath.");
            }

            return configuredPath;
        }

        var candidates = new[]
        {
            appDataDirectory.GetPath("profile.yaml"),
            appDataDirectory.GetPath("profile.yml"),
            appDataDirectory.GetPath("profile.json")
        };
        var discovered = candidates.FirstOrDefault(File.Exists);
        return discovered
            ?? throw ProfileError(
                "$",
                "No candidate profile was found in the application data directory.",
                "Create profile.yaml or configure an absolute Profile:FilePath.");
    }

    private static CandidateProfile DeserializeProfile(string path, string content)
    {
        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".json" => JsonSerializer.Deserialize<CandidateProfile>(content, JsonOptions)
                    ?? throw ProfileError(
                        "$",
                        "The JSON profile is empty.",
                        "Provide a JSON object matching the candidate profile schema."),
                ".yaml" or ".yml" => new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .WithDuplicateKeyChecking()
                    .Build()
                    .Deserialize<CandidateProfile>(content)
                    ?? throw ProfileError(
                        "$",
                        "The YAML profile is empty.",
                        "Provide a YAML mapping matching the candidate profile schema."),
                _ => throw ProfileError(
                    "$",
                    $"Unsupported profile extension '{Path.GetExtension(path)}'.",
                    "Use a .yaml, .yml, or .json profile file.")
            };
        }
        catch (JsonException exception)
        {
            throw ProfileError(
                exception.Path ?? "$",
                "The JSON document is malformed or contains an unknown field.",
                "Correct the value at the reported path and retry.",
                exception);
        }
        catch (YamlException exception)
        {
            var pathText = exception.Start.Line > 0
                ? $"$ (line {exception.Start.Line}, column {exception.Start.Column})"
                : "$";
            throw ProfileError(
                pathText,
                "The YAML document is malformed or contains an unsupported value.",
                "Correct the value at the reported location and retry.",
                exception);
        }
    }

    private string? ResolveCvPath(string profilePath, string? profileCvPath)
    {
        var selectedPath = options.Value.SupplementalCvPath ?? profileCvPath;
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return null;
        }

        var resolvedPath = Path.IsPathFullyQualified(selectedPath)
            ? Path.GetFullPath(selectedPath)
            : Path.GetFullPath(
                Path.Combine(
                    Path.GetDirectoryName(profilePath)
                        ?? throw new InvalidOperationException(
                            "The profile path has no parent directory."),
                    selectedPath));
        if (!File.Exists(resolvedPath))
        {
            throw ProfileError(
                "$.supplementalCvPath",
                $"Supplemental CV file '{resolvedPath}' does not exist.",
                "Create the Markdown file or remove supplementalCvPath.");
        }

        if (!string.Equals(
            Path.GetExtension(resolvedPath),
            ".md",
            StringComparison.OrdinalIgnoreCase))
        {
            throw ProfileError(
                "$.supplementalCvPath",
                "The supplemental CV must be a Markdown file.",
                "Use a .md file.");
        }

        return resolvedPath;
    }

    private static async Task<string> ReadBoundedUtf8Async(
        string path,
        int maximumBytes,
        string fieldPath,
        string remediation,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > maximumBytes)
        {
            throw ProfileError(
                fieldPath,
                $"File size {fileInfo.Length} bytes exceeds the {maximumBytes}-byte limit.",
                remediation);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 8192,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var buffer = new MemoryStream(capacity: (int)Math.Min(fileInfo.Length, maximumBytes));
        var chunk = new byte[8192];
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > maximumBytes)
            {
                throw ProfileError(
                    fieldPath,
                    $"File grew beyond the {maximumBytes}-byte limit while it was read.",
                    remediation);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        try
        {
            return StrictUtf8.GetString(buffer.GetBuffer(), 0, total);
        }
        catch (DecoderFallbackException exception)
        {
            throw ProfileError(
                fieldPath,
                "The file is not valid UTF-8.",
                "Save the file as UTF-8 without invalid byte sequences.",
                exception);
        }
    }

    private static CandidateProfileValidationException ProfileError(
        string path,
        string message,
        string remediation,
        Exception? innerException = null)
    {
        var validationException = new CandidateProfileValidationException(
            [new ProfileValidationError(path, message, remediation)],
            innerException);
        return validationException;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            WriteIndented = false
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return jsonOptions;
    }

}
