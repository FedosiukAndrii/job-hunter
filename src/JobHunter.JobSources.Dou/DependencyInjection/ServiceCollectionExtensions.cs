using JobHunter.Application.Sources;
using JobHunter.JobSources.Dou.Configuration;
using JobHunter.JobSources.Dou.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobHunter.JobSources.Dou.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDouJobSource(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<DouOptions>()
            .Bind(configuration.GetSection(DouOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DouOptions>, DouOptionsValidator>();
        services.AddSingleton<DouRssParser>();
        services.AddHttpClient<DouJobSource>()
            .ConfigurePrimaryHttpMessageHandler(
                () => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression =
                        System.Net.DecompressionMethods.GZip
                        | System.Net.DecompressionMethods.Deflate,
                    ConnectTimeout = TimeSpan.FromSeconds(10)
                });
        services.AddHttpClient<IDouDetailPageEnricher, DouDetailPageEnricher>()
            .ConfigurePrimaryHttpMessageHandler(
                () => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression =
                        System.Net.DecompressionMethods.GZip
                        | System.Net.DecompressionMethods.Deflate,
                    ConnectTimeout = TimeSpan.FromSeconds(10)
                });
        if (configuration.GetValue<bool?>($"{DouOptions.SectionName}:Enabled") ?? true)
        {
            services.AddSingleton<IJobSource>(
                serviceProvider => serviceProvider.GetRequiredService<DouJobSource>());
        }

        return services;
    }
}
