namespace JobHunter.Application.Security;

public interface ISecretReader
{
    string? GetSecret(string configurationKey);
}
