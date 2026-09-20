namespace JobHunter.AI.Copilot.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    public string Provider { get; set; } = CopilotOptions.ProviderName;

    public int AnalysisTimeoutSeconds { get; set; } = 60;

    public double MinimumConfidence { get; set; } = 0.65;

    public int MinimumFitScore { get; set; } = 75;

    public int TransientFailureRetryMinutes { get; set; } = 60;
}
