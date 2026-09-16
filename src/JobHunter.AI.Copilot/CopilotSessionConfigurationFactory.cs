using GitHub.Copilot;
using Microsoft.Extensions.AI;
using JobHunter.AI.Copilot.Configuration;

namespace JobHunter.AI.Copilot;

internal static class CopilotSessionConfigurationFactory
{
    public const string SubmissionToolName = "submit_job_analysis";

    public static SessionConfig Create(
        CopilotOptions options,
        AIFunction submissionTool,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(submissionTool);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        return new SessionConfig
        {
            ClientName = "job-hunter",
            Model = string.IsNullOrWhiteSpace(options.Model)
                ? "auto"
                : options.Model.Trim(),
            Tools = [submissionTool],
            AvailableTools = new ToolSet().AddCustom(SubmissionToolName),
            ExcludedTools = new ToolSet()
                .AddBuiltIn("*")
                .AddMcp("*"),
            ExcludedBuiltInAgents = ["*"],
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = CopilotAnalysisPromptBuilder.SystemMessage
            },
            OnPermissionRequest = static (_, _) =>
                throw new InvalidOperationException(
                    "Job analysis denies every permission request."),
            WorkingDirectory = workingDirectory,
            AdditionalDirectories = [],
            EnableConfigDiscovery = false,
            EnableOnDemandInstructionDiscovery = false,
            EnableFileHooks = false,
            EnableHostGitOperations = false,
            EnableSessionStore = false,
            EnableSkills = false,
            IncludedBuiltinSkills = [],
            EnableSessionTelemetry = false,
            EnableExperimentalMode = false,
            SkipCustomInstructions = true,
            CoauthorEnabled = false,
            ManageScheduleEnabled = false,
            McpServers = new Dictionary<string, McpServerConfig>(StringComparer.Ordinal),
            McpOAuthTokenStorage = McpOAuthTokenStorageMode.InMemory,
            CustomAgents = [],
            SkillDirectories = [],
            PluginDirectories = [],
            InstructionDirectories = [],
            Commands = [],
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            LargeOutput = new LargeToolOutputConfig { Enabled = false },
            ToolSearch = new ToolSearchConfig { Enabled = false },
            Memory = new MemoryConfiguration { Enabled = false },
            SkipEmbeddingRetrieval = true,
            EmbeddingCacheStorage = EmbeddingCacheStorageMode.InMemory,
            EnableFileChangeTracking = false,
            Streaming = false,
            IncludeSubAgentStreamingEvents = false,
            EnableManagedSettings = false
        };
    }
}
