namespace JobHunter.Domain.Common;

public interface IConcurrencyTracked
{
    long ConcurrencyVersion { get; set; }
}
