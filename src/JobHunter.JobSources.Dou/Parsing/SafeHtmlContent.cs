using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace JobHunter.JobSources.Dou.Parsing;

public static class SafeHtmlContent
{
    private static readonly HashSet<string> RemovedElements = new(
        ["script", "style", "iframe", "object", "embed", "form", "input", "button", "meta", "link"],
        StringComparer.OrdinalIgnoreCase);

    public static SanitizedHtml Sanitize(string? html, int maximumCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharacters, 1);
        var bounded = Bound(html ?? string.Empty, maximumCharacters);
        var parser = new HtmlParser();
        var document = parser.ParseDocument($"<body>{bounded}</body>");

        foreach (var element in document.Body?.QuerySelectorAll("*").ToArray() ?? [])
        {
            if (RemovedElements.Contains(element.LocalName))
            {
                element.Remove();
                continue;
            }

            foreach (var attribute in element.Attributes.ToArray())
            {
                if (attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name.Equals("style", StringComparison.OrdinalIgnoreCase)
                    || IsUnsafeUrlAttribute(attribute))
                {
                    element.RemoveAttribute(attribute.Name);
                }
            }
        }

        var sanitizedHtml = Bound(document.Body?.InnerHtml ?? string.Empty, maximumCharacters);
        var plainText = CollapseWhitespace(document.Body?.TextContent ?? string.Empty);
        return new SanitizedHtml(sanitizedHtml, Bound(plainText, maximumCharacters));
    }

    private static bool IsUnsafeUrlAttribute(IAttr attribute)
    {
        if (!attribute.Name.Equals("href", StringComparison.OrdinalIgnoreCase)
            && !attribute.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !Uri.TryCreate(attribute.Value, UriKind.Absolute, out var uri)
            || (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Bound(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters ? value : value[..maximumCharacters];
}

public sealed record SanitizedHtml(string Html, string PlainText);
