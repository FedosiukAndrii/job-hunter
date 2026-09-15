using System.Reflection;

namespace JobHunter.ContractTests.Architecture;

public sealed class SourceAdapterDependencyTests
{
    [Theory]
    [InlineData("JobHunter.JobSources.Dou")]
    [InlineData("JobHunter.JobSources.JobSpy")]
    public void SourceAdapterDoesNotReferencePersistenceOrNotificationAdapters(string assemblyName)
    {
        var referencedAssemblies = Assembly
            .Load(assemblyName)
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("JobHunter.Infrastructure", referencedAssemblies);
        Assert.DoesNotContain("JobHunter.Notifications.Telegram", referencedAssemblies);
        Assert.DoesNotContain("JobHunter.AI.Copilot", referencedAssemblies);
    }
}
