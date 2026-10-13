using System.Text.RegularExpressions;

namespace Lockpick.App.Services;

public sealed record CreditEntry(string Name, string Detail);

public sealed record CreditSection(string Title, IReadOnlyList<CreditEntry> Entries)
{
    public bool IsEmpty => Entries.Count == 0;
}

/// <summary>
/// In-app credits, read from CONTRIBUTORS.md (embedded at build time) so there is one list to maintain.
/// Each <c>## Section</c> becomes a group; each <c>- **Name** — detail</c> line becomes an entry.
/// </summary>
public static partial class Credits
{
    private const string ResourceName = "Lockpick.App.CONTRIBUTORS.md";

    /// <summary>Sections that only point elsewhere (e.g. research acknowledgements) are not shown as people.</summary>
    private static readonly HashSet<string> SkippedSections = new(StringComparer.OrdinalIgnoreCase) { "Research acknowledgements" };

    public static IReadOnlyList<CreditSection> Load()
    {
        using var stream = typeof(Credits).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static IReadOnlyList<CreditSection> Parse(string markdown)
    {
        var sections = new List<CreditSection>();
        string? title = null;
        var entries = new List<CreditEntry>();

        void Flush()
        {
            if (title is not null && !SkippedSections.Contains(title))
            {
                sections.Add(new CreditSection(title, entries.ToList()));
            }

            entries.Clear();
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                title = line[3..].Trim();
                continue;
            }

            var match = EntryPattern().Match(line);
            if (title is not null && match.Success)
            {
                entries.Add(new CreditEntry(match.Groups["name"].Value.Trim(), match.Groups["detail"].Value.Trim()));
            }
        }

        Flush();
        return sections;
    }

    [GeneratedRegex(@"^\s*[-*]\s+\*\*(?<name>[^*]+)\*\*\s*(?:[—–-]+\s*(?<detail>.*))?$")]
    private static partial Regex EntryPattern();
}
