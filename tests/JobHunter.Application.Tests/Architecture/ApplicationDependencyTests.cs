using JobHunter.Application.Storage;

namespace JobHunter.Application.Tests.Architecture;

public sealed class ApplicationDependencyTests
{
    [Fact]
    public void ApplicationAssemblyDoesNotReferenceInfrastructureFrameworks()
    {
        var referencedAssemblies = typeof(IAppDataDirectory)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", referencedAssemblies);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", referencedAssemblies);
        Assert.DoesNotContain("Telegram.Bot", referencedAssemblies);
        Assert.DoesNotContain("GitHub.Copilot.SDK", referencedAssemblies);
    }
}
