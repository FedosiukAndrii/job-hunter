namespace JobHunter.AI.Copilot.Configuration;

public sealed class CopilotOptions
{
    public const string SectionName = "AI:Copilot";

    public const string ProviderName = "copilot";

    public const string GitHubTokenConfigurationKey = "AI:Copilot:GitHubToken";

    public string? Model { get; set; }

    public bool FailStartupWhenModelUnavailable { get; set; }

    public int MaximumInputCharacters { get; set; } = 48_000;

    public int MaximumOutputCharacters { get; set; } = 4_000;

    public int MaximumConcurrency { get; set; } = 1;

    public int QueueCapacity { get; set; } = 32;

    public int MaximumTransientRetries { get; set; } = 1;

    public int TransientRetryDelaySeconds { get; set; } = 2;
}
