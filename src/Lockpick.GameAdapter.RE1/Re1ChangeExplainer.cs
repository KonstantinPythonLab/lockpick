using System.Globalization;
using System.Text.Json;
using Lockpick.Core.Insight;
using Lockpick.Core.State;

namespace Lockpick.GameAdapter.RE1;

/// <summary>
/// What RE1's own data says each flag means: the room index (which item, enemy or door uses each flag, generated from
/// the game's room files by tools/rdt), item/enemy names and the flag labels confirmed in play. Embedded from data/.
/// </summary>
internal sealed class Re1Knowledge
{
    private const string ResourcePrefix = "Lockpick.GameAdapter.RE1.Data.";

    private Re1Knowledge(JsonDocument index, JsonDocument names, JsonDocument labels)
    {
        static (string Room, string Player, int Value) Entry(JsonElement e, string key) =>
            (e.GetProperty("room").GetString()!, e.GetProperty("player").GetString()!, e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1);

        ItemFlags = [.. index.RootElement.GetProperty("items").EnumerateArray().Select(e => Entry(e, "flag"))];
        EnemyFlags = [.. index.RootElement.GetProperty("enemies").EnumerateArray().Select(e => Entry(e, "flag"))];
        LockFlags = [.. index.RootElement.GetProperty("doors").EnumerateArray().Select(e => Entry(e, "lockFlag"))];
        ItemIds = [.. names.RootElement.GetProperty("items").EnumerateObject().Select(p => int.Parse(p.Name, CultureInfo.InvariantCulture))];
        ItemIds.UnionWith(names.RootElement.GetProperty("maps").EnumerateObject().Select(p => int.Parse(p.Name, CultureInfo.InvariantCulture)));
        EnemyTypes = [.. names.RootElement.GetProperty("enemies").EnumerateObject().Select(p => int.Parse(p.Name, CultureInfo.InvariantCulture))];
        Labels = labels.RootElement.GetProperty("labels").EnumerateArray()
            .ToDictionary(e => (e.GetProperty("bank").GetString()!, Convert.ToUInt32(e.GetProperty("mask").GetString(), 16)), e => e.GetProperty("label").GetString()!);
    }

    public List<(string Room, string Player, int Value)> ItemFlags { get; }

    public List<(string Room, string Player, int Value)> EnemyFlags { get; }

    public List<(string Room, string Player, int Value)> LockFlags { get; }

    public HashSet<int> ItemIds { get; }

    public HashSet<int> EnemyTypes { get; }

    public Dictionary<(string Bank, uint Mask), string> Labels { get; }

    public static Re1Knowledge LoadEmbedded()
    {
        var assembly = typeof(Re1Knowledge).Assembly;
        JsonDocument Load(string file)
        {
            using var stream = assembly.GetManifestResourceStream(ResourcePrefix + file)
                ?? throw new InvalidOperationException($"Missing embedded RE1 data {file}.");
            return JsonDocument.Parse(stream);
        }

        using var index = Load("room-index.json");
        using var names = Load("names.json");
        using var labels = Load("flag-labels.json");
        return new Re1Knowledge(index, names, labels);
    }
}

/// <summary>
/// Flags out of RE1's flag banks that the room index and the confirmed labels don't explain — story/event flags,
/// unexpected bits — plus item ids and enemy types with no known name. Flag n is bit 31 − n % 32 of word n / 32.
/// </summary>
internal sealed class Re1ChangeExplainer(Re1Knowledge knowledge) : IChangeExplainer
{
    private static readonly (string Bank, int Words, string Kind)[] Banks =
    [
        ("flagRoomItems", 8, "item"), ("flagEnemies", 8, "enemy"), ("flagLocks", 2, "lock"), ("flagRoom", 5, "room"),
        ("flagScenarioA", 4, "story A"), ("flagScenarioB", 8, "story B"),
    ];

    public IReadOnlyList<UnexplainedChange> Unexplained(GameSnapshot previous, GameSnapshot current)
    {
        if (previous.InGameplay is not { HasValue: true, Value: true } || current.InGameplay is not { HasValue: true, Value: true }
            || !current.Location.TryGet(out var location) || !previous.Location.TryGet(out var previousLocation)
            || (current.GameTime.TryGet(out var now) && previous.GameTime.TryGet(out var before) && now < before))
        {
            return []; // not playing, or a new game / loaded save (everything resets at once)
        }

        var room = location.Id;
        var player = Raw(current, "character") is { } c && (c & 3) == 1 ? "jill" : "chris";
        var changes = new List<UnexplainedChange>();
        foreach (var (bank, words, kind) in Banks)
        {
            for (var word = 0; word < words; word++)
            {
                var key = $"{bank}{word}";
                if (Raw(previous, key) is not { } old || Raw(current, key) is not { } value || old == value)
                {
                    continue;
                }

                for (var bit = 0; bit < 32; bit++)
                {
                    var mask = 1u << bit;
                    if (((old ^ value) & mask) == 0)
                    {
                        continue;
                    }

                    var set = (value & mask) != 0;
                    var flag = (word * 32) + (31 - bit);
                    if (!Explains(bank, word, mask, flag, set, room, player))
                    {
                        changes.Add(new UnexplainedChange(
                            $"{key}:0x{mask:X8}:{(set ? "set" : "clear")}",
                            $"{kind} flag #{flag} {(set ? "set" : "cleared")} ({key} bit 0x{mask:X}) in room {room}"));
                    }
                }
            }
        }

        // Ids the name tables don't know. Enemies only once the room has loaded (same room in both polls).
        for (var slot = 0; slot < 8; slot++)
        {
            if (Raw(current, $"inv{slot}") is { } entry && (entry & 0xFF) is var id and > 0 && !knowledge.ItemIds.Contains((int)id))
            {
                changes.Add(new UnexplainedChange($"item:0x{id:X2}", $"unknown item id 0x{id:X2} in inventory slot {slot + 1}"));
            }

            if (previousLocation == location && Raw(current, $"enemy{slot}Flags") is { } flags && (flags & 1) != 0
                && Raw(current, $"enemy{slot}Type") is { } type && !knowledge.EnemyTypes.Contains((int)type))
            {
                changes.Add(new UnexplainedChange($"enemy:{type}", $"unknown enemy type {type} in room {room}"));
            }
        }

        return changes;
    }

    private static uint? Raw(GameSnapshot snapshot, string field) =>
        snapshot.RawFields.TryGetValue(field, out var value) && value.HasValue ? (uint)value.Value : null;

    private static bool Uses(List<(string Room, string Player, int Value)> entries, int flag, string player) =>
        entries.Exists(e => e.Value == flag && (e.Player == player || e.Player == "both"));

    private bool Explains(string bank, int word, uint mask, int flag, bool set, string room, string player)
    {
        if (knowledge.Labels.ContainsKey(($"{bank}{word}", mask)))
        {
            return true;
        }

        return bank switch
        {
            "flagRoomItems" => !set && Uses(knowledge.ItemFlags, flag, player),
            "flagEnemies" => set && Uses(knowledge.EnemyFlags, flag, player),
            "flagLocks" => Uses(knowledge.LockFlags, flag, player),

            // Rooms visited: one word per stage, room n = bit 31 - n, set on the first visit.
            "flagRoom" => set && word == room[0] - '1' && 31 - BitIndex(mask) == int.Parse(room.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            _ => false,
        };
    }

    private static int BitIndex(uint mask) => System.Numerics.BitOperations.TrailingZeroCount(mask);
}
