using JobHunter.Application.Security;
using Microsoft.Extensions.Configuration;

namespace JobHunter.Infrastructure.Security;

public sealed class ConfigurationSecretReader(IConfiguration configuration) : ISecretReader
{
    public string? GetSecret(string configurationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationKey);
        var value = configuration[configurationKey];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
