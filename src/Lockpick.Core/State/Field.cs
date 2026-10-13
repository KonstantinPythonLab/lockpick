using System.Text.Json.Serialization;

namespace Lockpick.Core.State;

/// <summary>Quality of a single game-state value.</summary>
public enum FieldStatus
{
    /// <summary>The active version profile has no verified source for this value.</summary>
    Unsupported = 0,

    /// <summary>The value is supported but could not be read or is not meaningful right now.</summary>
    Unavailable,

    /// <summary>The value was read successfully this poll.</summary>
    Valid,

    /// <summary>The latest read failed; <see cref="Field{T}.Value"/> holds the last good value.</summary>
    Stale,
}

/// <summary>
/// A game-state value tagged with its quality. Consumers must check <see cref="Status"/>
/// before trusting <see cref="Value"/>; only <see cref="FieldStatus.Valid"/> values are current.
/// </summary>
public readonly record struct Field<T>(FieldStatus Status, T Value)
{
    public static Field<T> Unsupported => new(FieldStatus.Unsupported, default!);

    public static Field<T> Unavailable => new(FieldStatus.Unavailable, default!);

    public static Field<T> Valid(T value) => new(FieldStatus.Valid, value);

    [JsonIgnore]
    public bool IsValid => Status == FieldStatus.Valid;

    /// <summary>True when <see cref="Value"/> carries a real value (current or last known).</summary>
    [JsonIgnore]
    public bool HasValue => Status is FieldStatus.Valid or FieldStatus.Stale;

    /// <summary>
    /// Produces the next field state after a failed read: a previously known value becomes stale,
    /// anything else stays unavailable.
    /// </summary>
    public Field<T> AsFailedRead() => HasValue ? new(FieldStatus.Stale, Value) : Unavailable;

    public bool TryGet(out T value)
    {
        value = Value;
        return IsValid;
    }

    public override string ToString() => Status switch
    {
        FieldStatus.Valid => Value?.ToString() ?? "null",
        FieldStatus.Stale => $"{Value} (stale)",
        _ => Status.ToString(),
    };
}
