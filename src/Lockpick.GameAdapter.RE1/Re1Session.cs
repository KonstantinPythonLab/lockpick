using Lockpick.Core.Adapters;
using Lockpick.Core.Memory;
using Lockpick.Core.Profiles;
using Lockpick.Core.State;

namespace Lockpick.GameAdapter.RE1;

/// <summary>Reads RE1 profile fields and maps them onto the normalized <see cref="GameSnapshot"/>.</summary>
internal sealed class Re1Session : IGameSession
{
    internal static class Fields
    {
        public const string Stage = "stage";
        public const string Room = "room";
        public const string Camera = "camera";
        public const string Hp = "hp";
        public const string MaxHp = "maxHp";
        public const string Character = "character";
        public const string GameTimer = "gameTimer";
        public const string StateFlags = "stateFlags";
        public const string MainMenu = "mainMenu";
        public const string PositionX = "positionX";
        public const string PositionY = "positionY";
        public const string PositionZ = "positionZ";
    }

    internal const int FramesPerSecond = 30;
    internal const int StageCount = 7;
    internal const int MaxRoom = 0x1F;

    private readonly IProcessMemory _memory;
    private readonly IReadOnlyList<FieldSpec> _fields;
    private readonly HashSet<string> _trusted;
    private readonly Dictionary<string, Field<long>> _lastRaw = new(StringComparer.OrdinalIgnoreCase);
    private GameSnapshot? _previous;

    public Re1Session(IProcessMemory memory, VersionProfile profile)
    {
        _memory = memory;
        Profile = profile;
        _fields = profile.Fields;
        _trusted = new HashSet<string>(profile.Fields.Where(f => f.IsTrusted).Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
    }

    public VersionProfile Profile { get; }

    public GameSnapshot Read(long sequence, DateTimeOffset now)
    {
        var raw = new Dictionary<string, Field<long>>(_fields.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var spec in _fields)
        {
            Field<long> value;
            if (_memory.TryReadValue(spec.Address, spec.Type, out var v))
            {
                value = Field<long>.Valid(spec.Mask is { } mask ? v & mask : v);
            }
            else
            {
                value = _lastRaw.TryGetValue(spec.Name, out var last) ? last.AsFailedRead() : Field<long>.Unavailable;
            }

            raw[spec.Name] = value;
            _lastRaw[spec.Name] = value;
        }

        var snapshot = Interpret(raw, sequence, now);
        _previous = snapshot;
        return snapshot;
    }

    private GameSnapshot Interpret(Dictionary<string, Field<long>> raw, long sequence, DateTimeOffset now)
    {
        var previous = _previous ?? new GameSnapshot();
        var gameplay = DeriveInGame(raw, previous.InGameplay);

        return new GameSnapshot
        {
            Sequence = sequence,
            CapturedAt = now,
            RawFields = raw,
            InGameplay = gameplay,
            Location = Derive(raw, gameplay, previous.Location, [Fields.Stage, Fields.Room], v =>
                v[0] < StageCount && v[1] <= MaxRoom ? new RoomLocation((int)v[0] + 1, (int)v[1]) : null),
            Camera = Derive(raw, gameplay, previous.Camera, [Fields.Camera], v => (int?)v[0]),
            Health = DeriveHealth(raw, gameplay, previous.Health),
            Character = Derive(raw, gameplay, previous.Character, [Fields.Character], v => v[0] switch
            {
                0 => PlayableCharacter.Chris,
                1 => PlayableCharacter.Jill,
                3 => PlayableCharacter.Rebecca,
                _ => PlayableCharacter.Unknown,
            }),
            Position = Derive(raw, gameplay, previous.Position, [Fields.PositionX, Fields.PositionY, Fields.PositionZ], v =>
                new Position((int)v[0], (int)v[1], (int)v[2])),
            GameTime = Derive(raw, gameplay, previous.GameTime, [Fields.GameTimer], v =>
                TimeSpan.FromSeconds(v[0] / (double)FramesPerSecond)),
        };
    }

    /// <summary>
    /// A game is in session when the title menu is not shown, a character has been set up (max HP
    /// is 0 from boot until New Game/Load) and the player is alive. After a death the previous
    /// game's values (e.g. HP -10) stay in memory through character select and the intro, so HP &gt; 0
    /// is required to avoid reporting them. The decomp's 0x02000000 "gameplay" flag was refuted by
    /// live traces and is deliberately not used; see docs/RESEARCH.md.
    /// </summary>
    private Field<bool> DeriveInGame(Dictionary<string, Field<long>> raw, Field<bool> previous)
    {
        if (Trusted(raw, Fields.MainMenu) is not { } menu
            || Trusted(raw, Fields.MaxHp) is not { } maxHp
            || Trusted(raw, Fields.Hp) is not { } hp)
        {
            return Field<bool>.Unsupported;
        }

        if (!menu.IsValid || !maxHp.IsValid || !hp.IsValid)
        {
            return previous.AsFailedRead();
        }

        return Field<bool>.Valid(menu.Value == 0 && maxHp.Value > 0 && hp.Value > 0);
    }

    private Field<Health> DeriveHealth(Dictionary<string, Field<long>> raw, Field<bool> gameplay, Field<Health> previous)
    {
        var health = Derive(raw, gameplay, previous, [Fields.Hp], v => new Health((int)v[0], null));
        if (health.IsValid && Trusted(raw, Fields.MaxHp) is { IsValid: true } max && max.Value > 0)
        {
            return Field<Health>.Valid(health.Value with { Maximum = (int)max.Value });
        }

        return health;
    }

    /// <summary>
    /// Builds a normalized field from trusted raw inputs. Unsupported if any input lacks a trusted profile
    /// field; unavailable outside gameplay or when the value fails sanity checks; stale when a read failed.
    /// </summary>
    private Field<T> Derive<T>(
        Dictionary<string, Field<long>> raw,
        Field<bool> gameplay,
        Field<T> previous,
        string[] inputs,
        Func<long[], T?> map)
        where T : struct
    {
        var values = new long[inputs.Length];
        var readFailed = false;
        for (var i = 0; i < inputs.Length; i++)
        {
            if (Trusted(raw, inputs[i]) is not { } field)
            {
                return Field<T>.Unsupported;
            }

            if (!field.IsValid)
            {
                readFailed = true;
                continue;
            }

            values[i] = field.Value;
        }

        if (readFailed || gameplay.Status is FieldStatus.Stale or FieldStatus.Unavailable)
        {
            return previous.AsFailedRead();
        }

        // Outside gameplay these addresses hold leftovers from the previous session or attract mode.
        if (gameplay is { IsValid: true, Value: false })
        {
            return Field<T>.Unavailable;
        }

        return map(values) is { } mapped ? Field<T>.Valid(mapped) : Field<T>.Unavailable;
    }

    private Field<long>? Trusted(Dictionary<string, Field<long>> raw, string name) =>
        _trusted.Contains(name) && raw.TryGetValue(name, out var value) ? value : null;
}
