using Lockpick.App.Services;

namespace Lockpick.Tests.App;

public class CreditsTests
{
    [Fact]
    public void Parses_sections_and_entries_and_skips_research_acknowledgements()
    {
        var sections = Credits.Parse("""
            # Contributors

            Intro text.

            ## Project

            - **mscrnt** — project owner; live verification sessions

            ## Gameplay captures and testing

            Volunteers who send capture sessions are listed here.
            - **someone** — captures (B102, B109)
            - **plain-name**

            ## Research acknowledgements

            See THIRD_PARTY_NOTICES.md.
            """);

        Assert.Equal(["Project", "Gameplay captures and testing"], sections.Select(s => s.Title));
        Assert.Equal(new CreditEntry("mscrnt", "project owner; live verification sessions"), sections[0].Entries.Single());
        Assert.Equal(["someone", "plain-name"], sections[1].Entries.Select(e => e.Name));
        Assert.Equal("captures (B102, B109)", sections[1].Entries[0].Detail);
        Assert.Equal(string.Empty, sections[1].Entries[1].Detail);
    }

    [Fact]
    public void Empty_sections_are_kept_so_the_app_can_invite_contributors()
    {
        var sections = Credits.Parse("## Gameplay captures and testing\n\nNobody yet.\n");
        Assert.True(Assert.Single(sections).IsEmpty);
    }

    [Fact]
    public void The_real_contributors_file_is_embedded_and_parses()
    {
        var sections = Credits.Load();
        Assert.Contains(sections, s => s.Title == "Project" && s.Entries.Any(e => e.Name == "mscrnt"));
        Assert.DoesNotContain(sections, s => s.Title == "Research acknowledgements");
    }
}
