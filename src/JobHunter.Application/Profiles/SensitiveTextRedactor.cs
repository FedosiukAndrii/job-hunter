using System.Text.RegularExpressions;

namespace JobHunter.Application.Profiles;

public static partial class SensitiveTextRedactor
{
    public static string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var redacted = EmailPattern().Replace(text, "[redacted-email]");
        redacted = PhonePattern().Replace(redacted, "[redacted-phone]");
        return AddressLinePattern().Replace(redacted, "${label}[redacted-address]");
    }

    [GeneratedRegex(
        @"(?<![\w.+-])[\w.+-]+@[\w-]+(?:\.[\w-]+)+(?![\w-])",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(
        @"(?<!\w)(?:\+?\d[\d ()-]{7,}\d)(?!\w)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(
        @"^(?<label>\s*(?:address|postal address|home address|адреса)\s*:\s*).+$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AddressLinePattern();
}
