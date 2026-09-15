using JobHunter.Application.Persistence;
using JobHunter.Application.Runtime;
using JobHunter.Application.Storage;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Persistence;
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

        return services;
    }
}
