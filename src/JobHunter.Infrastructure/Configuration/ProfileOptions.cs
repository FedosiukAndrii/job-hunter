namespace JobHunter.Infrastructure.Configuration;

public sealed class ProfileOptions
{
    public const string SectionName = "Profile";

    public string? FilePath { get; set; }

    public string? SupplementalCvPath { get; set; }

    public int MaximumProfileBytes { get; set; } = 128 * 1024;

    public int MaximumCvBytes { get; set; } = 256 * 1024;
}
