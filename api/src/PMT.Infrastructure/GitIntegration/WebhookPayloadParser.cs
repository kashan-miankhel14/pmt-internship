using System.Text.RegularExpressions;
namespace PMT.Infrastructure.GitIntegration;
public static partial class WebhookPayloadParser
{
    [GeneratedRegex(@"\bPMT-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegex();
    public static IReadOnlyCollection<long> FindPmtReferences(string text) => ReferenceRegex().Matches(text ?? string.Empty).Select(x => long.Parse(x.Groups[1].Value)).Distinct().ToArray();
}
