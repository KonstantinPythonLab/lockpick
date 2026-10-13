using Lockpick.Core.Insight;
using Lockpick.Core.State;
using Lockpick.GameAdapter.RE1;

namespace Lockpick.Tests.RE1;

/// <summary>RE1's ask-mode knowledge: what the room index and confirmed labels explain, and what gets asked about.</summary>
public class Re1ChangeExplainerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly IChangeExplainer _explainer;

    public Re1ChangeExplainerTests()
    {
        var adapter = new Re1Adapter();
        _explainer = adapter.CreateChangeExplainer(adapter.Profiles[0]);
    }

    /// <summary>Jill in the dining room (105), the values seen live before she took the blue jewel.</summary>
    private static GameSnapshot Jill(double seconds, string room = "105", params (string Field, long Value)[] raw)
    {
        var fields = new Dictionary<string, Field<long>>
        {
            ["character"] = Field<long>.Valid(1),
            ["flagRoomItems1"] = Field<long>.Valid(0xFEDFFFFF),
            ["flagScenarioB0"] = Field<long>.Valid(0x80000000),
            ["flagRoom0"] = Field<long>.Valid(0x06000000),
            ["flagRoom3"] = Field<long>.Valid(0),
            ["inv0"] = Field<long>.Valid(0x0001),
        };
        foreach (var (field, value) in raw)
        {
            fields[field] = Field<long>.Valid(value);
        }

        return new GameSnapshot
        {
            CapturedAt = T0.AddSeconds(seconds),
            InGameplay = Field<bool>.Valid(true),
            Location = Field<RoomLocation>.Valid(new RoomLocation(room[0] - '0', Convert.ToInt32(room[1..], 16))),
            GameTime = Field<TimeSpan>.Valid(TimeSpan.FromSeconds(100 + seconds)),
            RawFields = fields,
        };
    }

    [Fact]
    public void Pickups_from_the_room_index_are_explained()
    {
        var after = Jill(1, raw: ("flagRoomItems1", 0xFED7FFFF)); // blue jewel, item flag 44
        Assert.Empty(_explainer.Unexplained(Jill(0), after));
    }

    [Fact]
    public void Story_flags_are_unexplained_unless_confirmed()
    {
        var change = Assert.Single(_explainer.Unexplained(Jill(0), Jill(1, raw: ("flagScenarioB0", 0xA0000000))));
        Assert.Equal("flagScenarioB0:0x20000000:set", change.Key);
        Assert.Contains("story B flag #2 set", change.Description, StringComparison.Ordinal);

        // The statue falling is a confirmed label: not asked about.
        Assert.Empty(_explainer.Unexplained(Jill(0, "202"), Jill(1, "202", ("flagScenarioA0", 0x00100000))));
    }

    [Fact]
    public void First_visits_are_explained_but_other_room_bank_bits_are_not()
    {
        Assert.Empty(_explainer.Unexplained(Jill(0, "104"), Jill(1, "104", ("flagRoom0", 0x0E000000)))); // room 104 = bit 27
        var odd = Assert.Single(_explainer.Unexplained(Jill(0, "107"), Jill(1, "107", ("flagRoom3", 0x8))));
        Assert.Equal("flagRoom3:0x00000008:set", odd.Key);
    }

    [Fact]
    public void Unknown_items_and_enemies_are_reported()
    {
        var changes = _explainer.Unexplained(Jill(0), Jill(1, raw: [("inv3", 0x014C), ("enemy0Flags", 1), ("enemy0Type", 60)]));
        Assert.Contains(changes, c => c.Key == "item:0x4C");
        Assert.Contains(changes, c => c.Key == "enemy:60");
    }

    [Fact]
    public void Nothing_is_reported_across_a_new_game_or_load()
    {
        var later = Jill(0, raw: ("flagScenarioB0", 0xFFFFFFFF));
        var newGame = Jill(1) with { GameTime = Field<TimeSpan>.Valid(TimeSpan.Zero) };
        Assert.Empty(_explainer.Unexplained(later, newGame));
    }
}
