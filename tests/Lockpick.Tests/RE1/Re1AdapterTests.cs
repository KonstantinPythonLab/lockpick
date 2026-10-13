using Lockpick.Core.Adapters;
using Lockpick.Core.Processes;
using Lockpick.Core.Profiles;
using Lockpick.Core.State;
using Lockpick.GameAdapter.RE1;
using Lockpick.Tests.Fakes;

namespace Lockpick.Tests.RE1;

/// <summary>Exercises the real RE1 adapter against fake process memory laid out like the GOG build.</summary>
public class Re1AdapterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    private readonly Re1Adapter _adapter = new();
    private readonly VersionProfile _profile;
    private readonly FakeMemory _memory;
    private readonly IGameSession _session;

    public Re1AdapterTests()
    {
        _profile = _adapter.Profiles.Single();
        _memory = new FakeMemory().WithVerifiedImage(_profile);
        _session = _adapter.Attach(new FakeAttachedProcess(1, _memory, new ModuleImage(0x400000, 0x49B2000, null)), _profile);
    }

    private ulong Addr(string field) => _profile.FindField(field)!.Address;

    /// <summary>Values observed live in a new Chris game in the main hall (2026-10-12 trace).</summary>
    private void NewChrisGameInMainHall()
    {
        _memory.WriteU8(Addr("stage"), 0)
            .WriteU8(Addr("room"), 0x06)
            .WriteU8(Addr("camera"), 1)
            .WriteI16(Addr("hp"), 140)
            .WriteU8(Addr("maxHp"), 140)
            .WriteU8(Addr("character"), 0)
            .WriteU32(Addr("gameTimer"), 0x142)
            .WriteU32(Addr("stateFlags"), 0x8013000C)
            .WriteI32(Addr("positionX"), 0x3908)
            .WriteI32(Addr("positionZ"), 0x1388)
            .WriteU8(Addr("mainMenu"), 0);
    }

    [Fact]
    public void Reads_and_normalizes_a_live_like_game_state()
    {
        NewChrisGameInMainHall();

        var s = _session.Read(1, T0);

        Assert.Equal(Field<bool>.Valid(true), s.InGameplay);
        Assert.Equal(new RoomLocation(1, 0x06), s.Location.Value);
        Assert.Equal("106", s.Location.Value.Id);
        Assert.Equal(1, s.Camera.Value);
        Assert.Equal(new Health(140, 140), s.Health.Value);
        Assert.Equal(PlayableCharacter.Chris, s.Character.Value);
        Assert.Equal(new Position(0x3908, 0, 0x1388), s.Position.Value);
        Assert.Equal(TimeSpan.FromSeconds(0x142 / 30.0), s.GameTime.Value);
        Assert.All(new[] { s.Location.Status, s.Camera.Status, s.Health.Status, s.Character.Status, s.GameTime.Status }, st => Assert.Equal(FieldStatus.Valid, st));
    }

    [Theory]
    [InlineData(0x00, PlayableCharacter.Chris)]
    [InlineData(0x01, PlayableCharacter.Jill)]
    [InlineData(0x03, PlayableCharacter.Rebecca)]
    [InlineData(0x09, PlayableCharacter.Jill)] // alternate costume bit is masked off
    [InlineData(0x02, PlayableCharacter.Unknown)]
    public void Character_id_is_masked_and_mapped(byte raw, PlayableCharacter expected)
    {
        NewChrisGameInMainHall();
        _memory.WriteU8(Addr("character"), raw);
        Assert.Equal(expected, _session.Read(1, T0).Character.Value);
    }

    [Fact]
    public void Title_screen_marks_player_fields_unavailable()
    {
        NewChrisGameInMainHall();
        _memory.WriteU8(Addr("mainMenu"), 1);

        var s = _session.Read(1, T0);

        Assert.Equal(Field<bool>.Valid(false), s.InGameplay);
        Assert.Equal(FieldStatus.Unavailable, s.Health.Status);
        Assert.Equal(FieldStatus.Unavailable, s.Location.Status);
        Assert.Equal(FieldStatus.Unavailable, s.GameTime.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)] // observed live: the final bite overshoots to -10 and stays there through the next New Game intro
    public void Dead_player_is_not_in_game(short hp)
    {
        NewChrisGameInMainHall();
        _memory.WriteI16(Addr("hp"), hp).WriteU8(Addr("room"), 0x1B);

        var s = _session.Read(1, T0);
        Assert.Equal(Field<bool>.Valid(false), s.InGameplay);
        Assert.Equal(FieldStatus.Unavailable, s.Location.Status);
    }

    [Fact]
    public void Fresh_boot_with_zero_max_hp_is_not_in_game()
    {
        // Observed live: every field is zero before the first New Game.
        var s = _session.Read(1, T0);
        Assert.Equal(Field<bool>.Valid(false), s.InGameplay);
        Assert.Equal(FieldStatus.Unavailable, s.Location.Status);
    }

    [Fact]
    public void State_flag_0x2000000_does_not_affect_session_state()
    {
        NewChrisGameInMainHall();
        _memory.WriteU32(Addr("stateFlags"), 0x80000000); // free movement in room 105 had no 0x2000000 bit
        Assert.True(_session.Read(1, T0).InGameplay.Value);
    }

    [Fact]
    public void Out_of_range_stage_is_rejected_not_displayed()
    {
        NewChrisGameInMainHall();
        _memory.WriteU8(Addr("stage"), 9);
        Assert.Equal(FieldStatus.Unavailable, _session.Read(1, T0).Location.Status);
    }

    [Fact]
    public void Failed_reads_turn_previous_values_stale_then_recover()
    {
        NewChrisGameInMainHall();
        Assert.True(_session.Read(1, T0).Camera.IsValid);

        _memory.FailingAddresses.Add(Addr("camera"));
        var failed = _session.Read(2, T0);
        Assert.Equal(FieldStatus.Stale, failed.Camera.Status);
        Assert.Equal(1, failed.Camera.Value);
        Assert.True(failed.Location.IsValid); // other fields unaffected

        _memory.FailingAddresses.Clear();
        _memory.WriteU8(Addr("camera"), 4);
        Assert.Equal(Field<int>.Valid(4), _session.Read(3, T0).Camera);
    }

    [Fact]
    public void Session_gate_read_failure_makes_dependent_fields_stale()
    {
        NewChrisGameInMainHall();
        _session.Read(1, T0);
        _memory.FailingAddresses.Add(Addr("hp")); // hp, maxHp and mainMenu all gate the session

        var s = _session.Read(2, T0);
        Assert.Equal(FieldStatus.Stale, s.InGameplay.Status);
        Assert.Equal(FieldStatus.Stale, s.Location.Status);
    }

    [Fact]
    public void Unverified_fields_are_read_for_diagnostics_only()
    {
        NewChrisGameInMainHall();
        _memory.WriteI16(Addr("yaw"), 0x800);
        var s = _session.Read(1, T0);
        Assert.Equal(0x800, s.RawFields["yaw"].Value);
        Assert.Equal(FieldStatus.Valid, s.RawFields["enemyCount"].Status);
    }

    [Fact]
    public void Reads_stay_inside_the_verified_image()
    {
        // The validated memory wrapper must never let a read escape the module image.
        NewChrisGameInMainHall();
        var before = _memory.ReadCount;
        _session.Read(1, T0);
        Assert.Equal(_profile.Fields.Count, _memory.ReadCount - before);
    }

    [Fact]
    public void Attach_rejects_patched_code()
    {
        var memory = new FakeMemory().WithVerifiedImage(_profile);
        memory.Write(_profile.Anchors[0].Address, [0x90, 0x90, 0x90, 0x90, 0x90]);
        var process = new FakeAttachedProcess(2, memory, new ModuleImage(0x400000, 0x49B2000, null));
        var ex = Assert.Throws<AttachRejectedException>(() => _adapter.Attach(process, _profile));
        Assert.Contains(_profile.Anchors[0].Name, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void User_profiles_override_built_in_profiles_by_id()
    {
        var custom = _profile with { DisplayName = "Custom" };
        var other = _profile with { Id = "other-game", GameId = "re2" };
        var adapter = new Re1Adapter([custom, other]);
        Assert.Equal("Custom", Assert.Single(adapter.Profiles).DisplayName);
    }
}
