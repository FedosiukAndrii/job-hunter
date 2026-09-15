namespace JobHunter.Application.Runtime;

public sealed class ApplicationInstanceUnavailableException : Exception
{
    public ApplicationInstanceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
