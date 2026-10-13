using System.Buffers.Binary;

namespace Lockpick.Core.Fingerprinting;

public enum PeMachine : ushort
{
    Unknown = 0,
    X86 = 0x014C,
    X64 = 0x8664,
    Arm64 = 0xAA64,
}

/// <summary>Identity fields from a PE header. Parsed identically from a file or a mapped image.</summary>
public sealed record PeImageInfo(
    PeMachine Machine,
    uint TimeDateStamp,
    ulong ImageBase,
    uint SizeOfImage,
    uint EntryPointRva,
    ushort DllCharacteristics)
{
    /// <summary>Bytes from the start of the image that always cover the headers we parse.</summary>
    public const int HeaderProbeSize = 0x400;

    private const ushort DynamicBase = 0x0040;

    public bool Is32Bit => Machine == PeMachine.X86;

    /// <summary>True when the image opts into ASLR, meaning absolute addresses can move.</summary>
    public bool IsRelocatable => (DllCharacteristics & DynamicBase) != 0;

    public static bool TryParse(ReadOnlySpan<byte> image, out PeImageInfo? info)
    {
        info = null;
        if (image.Length < 0x40 || image[0] != (byte)'M' || image[1] != (byte)'Z')
        {
            return false;
        }

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(image[0x3C..]);
        if (peOffset <= 0 || peOffset > image.Length - 0x78)
        {
            return false;
        }

        var pe = image[peOffset..];
        if (BinaryPrimitives.ReadUInt32LittleEndian(pe) != 0x00004550) // "PE\0\0"
        {
            return false;
        }

        var machine = (PeMachine)BinaryPrimitives.ReadUInt16LittleEndian(pe[4..]);
        var timestamp = BinaryPrimitives.ReadUInt32LittleEndian(pe[8..]);
        var optional = pe[24..];
        var magic = BinaryPrimitives.ReadUInt16LittleEndian(optional);
        ulong imageBase;
        int dllCharacteristicsOffset;
        switch (magic)
        {
            case 0x10B: // PE32
                imageBase = BinaryPrimitives.ReadUInt32LittleEndian(optional[28..]);
                dllCharacteristicsOffset = 70;
                break;
            case 0x20B: // PE32+
                if (optional.Length < 0x48)
                {
                    return false;
                }

                imageBase = BinaryPrimitives.ReadUInt64LittleEndian(optional[24..]);
                dllCharacteristicsOffset = 70;
                break;
            default:
                return false;
        }

        info = new PeImageInfo(
            machine,
            timestamp,
            imageBase,
            SizeOfImage: BinaryPrimitives.ReadUInt32LittleEndian(optional[56..]),
            EntryPointRva: BinaryPrimitives.ReadUInt32LittleEndian(optional[16..]),
            DllCharacteristics: BinaryPrimitives.ReadUInt16LittleEndian(optional[dllCharacteristicsOffset..]));
        return true;
    }
}
