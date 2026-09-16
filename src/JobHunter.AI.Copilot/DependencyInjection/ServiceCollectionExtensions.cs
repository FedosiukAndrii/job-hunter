using JobHunter.AI.Abstractions;
using JobHunter.AI.Copilot.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobHunter.AI.Copilot.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCopilotJobAnalysis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();
        services
            .AddOptions<CopilotOptions>()
            .Bind(configuration.GetSection(CopilotOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<CopilotOptions>,
            CopilotOptionsValidator>();

        services.AddSingleton<NullJobAnalyzer>();
        services.AddSingleton<ICopilotSessionRunner, CopilotSessionRunner>();
        services.AddSingleton(
            serviceProvider => new CopilotJobAnalyzer(
                serviceProvider.GetRequiredService<ICopilotSessionRunner>(),
                serviceProvider.GetRequiredService<IOptions<CopilotOptions>>()));
        services.AddSingleton<IJobAnalyzer>(
            serviceProvider =>
            {
                var aiOptions =
                    serviceProvider.GetRequiredService<IOptions<AiOptions>>().Value;
                var disabled =
                    serviceProvider.GetRequiredService<NullJobAnalyzer>();
                var selector = new JobAnalyzerSelector(
                    [serviceProvider.GetRequiredService<CopilotJobAnalyzer>()],
                    disabled);
                return selector.Select(aiOptions.Enabled, aiOptions.Provider);
            });

        return services;
    }
}
