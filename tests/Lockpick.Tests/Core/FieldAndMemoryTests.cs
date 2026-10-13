using Lockpick.Core.Fingerprinting;
using Lockpick.Core.Memory;
using Lockpick.Core.State;
using Lockpick.Tests.Fakes;

namespace Lockpick.Tests.Core;

public class FieldTests
{
    [Fact]
    public void Failed_read_after_valid_value_becomes_stale_and_keeps_value()
    {
        var stale = Field<int>.Valid(42).AsFailedRead();
        Assert.Equal(FieldStatus.Stale, stale.Status);
        Assert.Equal(42, stale.Value);
        Assert.False(stale.IsValid);
        Assert.True(stale.HasValue);
    }

    [Fact]
    public void Failed_read_without_value_is_unavailable()
    {
        Assert.Equal(FieldStatus.Unavailable, Field<int>.Unsupported.AsFailedRead().Status);
        Assert.Equal(FieldStatus.Unavailable, Field<int>.Unavailable.AsFailedRead().Status);
    }

    [Fact]
    public void TryGet_only_succeeds_for_valid()
    {
        Assert.True(Field<int>.Valid(1).TryGet(out _));
        Assert.False(new Field<int>(FieldStatus.Stale, 1).TryGet(out _));
    }

    [Fact]
    public void Room_id_matches_rdt_naming()
    {
        Assert.Equal("106", new RoomLocation(1, 0x06).Id);
        Assert.Equal("21C", new RoomLocation(2, 0x1C).Id);
    }
}

public class MemoryTests
{
    [Fact]
    public void Typed_reads_are_little_endian_and_sign_aware()
    {
        var mem = new FakeMemory().WriteI16(0x1000, -5).WriteU32(0x2000, 0xDEADBEEF);
        Assert.True(mem.TryReadI16(0x1000, out var i16));
        Assert.Equal(-5, i16);
        Assert.True(mem.TryReadValue(0x1000, Lockpick.Core.Memory.ValueType.U16, out var u16));
        Assert.Equal(0xFFFB, u16);
        Assert.True(mem.TryReadU32(0x2000, out var u32));
        Assert.Equal(0xDEADBEEFu, u32);
    }

    [Fact]
    public void Failed_read_returns_false_and_zero()
    {
        var mem = new FakeMemory();
        Assert.False(mem.TryReadU32(0x1234, out var value));
        Assert.Equal(0u, value);
    }

    [Fact]
    public void Validated_memory_rejects_reads_outside_allowed_ranges()
    {
        var inner = new FakeMemory().WriteU32(0x400000, 1).WriteU32(0x900000, 2);
        var validated = new ValidatedMemory(inner, [new AddressRange(0x400000, 0x1000)]);

        Assert.True(validated.TryReadU32(0x400000, out _));
        Assert.False(validated.TryReadU32(0x900000, out _));
        Assert.False(validated.TryReadU32(0x400FFE, out _)); // straddles the end
        Assert.Equal(2, validated.RejectedReads);
    }

    [Fact]
    public void Address_range_handles_overflow()
    {
        var range = new AddressRange(ulong.MaxValue - 4, 4);
        Assert.False(range.Contains(ulong.MaxValue - 1, 8));
    }
}

public class PeImageInfoTests
{
    [Fact]
    public void Parses_pe32_identity_fields()
    {
        var header = PeHeaderBuilder.Build(0x33FD5643, 0x400000, 0x49B2000);
        Assert.True(PeImageInfo.TryParse(header, out var info));
        Assert.NotNull(info);
        Assert.Equal(PeMachine.X86, info.Machine);
        Assert.Equal(0x33FD5643u, info.TimeDateStamp);
        Assert.Equal(0x400000ul, info.ImageBase);
        Assert.Equal(0x49B2000u, info.SizeOfImage);
        Assert.False(info.IsRelocatable);
        Assert.True(info.Is32Bit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Rejects_garbage(int length)
    {
        Assert.False(PeImageInfo.TryParse(new byte[length], out _));
        var notPe = new byte[0x400];
        notPe[0] = (byte)'M';
        notPe[1] = (byte)'Z';
        notPe[0x3C] = 0x80;
        Assert.False(PeImageInfo.TryParse(notPe, out _));
    }

    [Fact]
    public void File_fingerprint_hashes_and_parses_header()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lockpick-test-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, PeHeaderBuilder.Build(0x12345678, 0x400000, 0x10000));
            var provider = new FileFingerprintProvider();
            var fp = provider.Compute(path);
            Assert.Equal(0x400, fp.FileSize);
            Assert.Equal(64, fp.Sha256.Length);
            Assert.Equal(0x12345678u, fp.Pe!.TimeDateStamp);
            Assert.Same(fp, provider.Compute(path)); // cached
        }
        finally
        {
            File.Delete(path);
        }
    }
}
