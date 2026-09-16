using JobHunter.Application.Persistence;
using JobHunter.Application.Evaluation;
using JobHunter.Application.Profiles;
using JobHunter.Application.Runtime;
using JobHunter.Application.Storage;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Profiles;
using JobHunter.Infrastructure.Runtime;
using JobHunter.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJobHunterInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
        services
            .AddOptions<ProfileOptions>()
            .Bind(configuration.GetSection(ProfileOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ProfileOptions>, ProfileOptionsValidator>();

        services.AddSingleton<IAppDataDirectory, PlatformAppDataDirectory>();
        services.AddSingleton<IApplicationInstanceGuard, SingleInstanceFileGuard>();
        services.AddPooledDbContextFactory<JobHunterDbContext>((serviceProvider, optionsBuilder) =>
        {
            var appDataDirectory = serviceProvider.GetRequiredService<IAppDataDirectory>();
            var storage = serviceProvider.GetRequiredService<IOptions<StorageOptions>>().Value;
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = appDataDirectory.GetPath(storage.DatabaseFileName),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true,
                ForeignKeys = true,
                DefaultTimeout = storage.BusyTimeoutSeconds
            }.ToString();

            optionsBuilder.UseSqlite(
                connectionString,
                sqliteOptions => sqliteOptions.MigrationsAssembly(
                    typeof(JobHunterDbContext).Assembly.FullName));
        });
        services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();
        services.AddSingleton<IDatabaseMaintenance, SqliteDatabaseMaintenance>();
        services.AddSingleton<IJobIngestionStore, EfJobIngestionStore>();
        services.AddSingleton<ISourceRunStore, EfSourceRunStore>();
        services.AddSingleton<ICandidateProfileLoader, FileCandidateProfileLoader>();
        services.AddSingleton<ICandidateProfileStore, EfCandidateProfileStore>();
        services.AddSingleton<IRuleEvaluationStore, EfRuleEvaluationStore>();
        services.AddSingleton<DeterministicJobEvaluator>();

        return services;
    }
}
