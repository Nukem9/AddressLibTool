namespace AddressLibTool;

using System.Text;

/// <summary>
/// Parser for bin files created by https://www.nexusmods.com/skyrimspecialedition/mods/32444. Direct port from
/// C++ code.
/// </summary>
public struct AddrLibBin
{
    private const int SupportedFormat = 1;

    /// <summary>
    /// Maps an ID to an address
    /// </summary>
    private Dictionary<ulong, ulong> _idToAddress { get; init; }

    /// <summary>
    /// Maps an address to an ID
    /// </summary>
    private Dictionary<ulong, ulong> _addressToId { get; init; }

    public ulong GetAddressFromId(ulong Id)
    {
        return _idToAddress[Id];
    }

    public ulong GetIdFromAddress(ulong address)
    {
        return _addressToId[address];
    }

    public static AddrLibBin LoadFile(string path)
    {
        using var fileStream = File.OpenRead(path);
        using var reader = new BinaryReader(fileStream);

        // Parse header
        int format = reader.ReadInt32();

        if (format != SupportedFormat)
            throw new NotSupportedException("Unknown bin file format");

        int[] version = new int[4];

        for (int i = 0; i < 4; i++)
            version[i] = reader.ReadInt32();

        int nameLen = reader.ReadInt32();

        if (nameLen < 0 || nameLen > 10000)
            throw new InvalidDataException("Name length is unexpected");

        string executableName = Encoding.UTF8.GetString(reader.ReadBytes(nameLen));

        // Parse bit-encoded IDs and addresses
        return ReadEncodedAddressData(reader, version);
    }

    private static AddrLibBin ReadEncodedAddressData(BinaryReader reader, int[] version)
    {
        int pointerSize = reader.ReadInt32();
        int addressCount = reader.ReadInt32();

        byte b1 = 0;
        byte b2 = 0;

        ushort w1 = 0;
        ushort w2 = 0;

        uint d1 = 0;
        uint d2 = 0;

        ulong q1 = 0;
        ulong q2 = 0;

        ulong pvid = 0;
        ulong poffset = 0;
        ulong tpoffset = 0;

        var addrMap = new Dictionary<ulong, ulong>();
        var idMap = new Dictionary<ulong, ulong>();

        for (int i = 0; i < addressCount; i++)
        {
            var type = reader.ReadByte();

            var low = type & 0xF;
            var high = type >> 4;

            switch (low)
            {
                case 0: q1 = reader.ReadUInt64(); break;
                case 1: q1 = pvid + 1; break;
                case 2: b1 = reader.ReadByte(); q1 = pvid + b1; break;
                case 3: b1 = reader.ReadByte(); q1 = pvid - b1; break;
                case 4: w1 = reader.ReadUInt16(); q1 = pvid + w1; break;
                case 5: w1 = reader.ReadUInt16(); q1 = pvid - w1; break;
                case 6: w1 = reader.ReadUInt16(); q1 = w1; break;
                case 7: d1 = reader.ReadUInt32(); q1 = d1; break;
                default:
                    throw new InvalidDataException("Unknown low nibble encoding type");
            }

            tpoffset = (high & 8) != 0 ? (poffset / (ulong)pointerSize) : poffset;

            switch (high & 7)
            {
                case 0: q2 = reader.ReadUInt64(); break;
                case 1: q2 = tpoffset + 1; break;
                case 2: b2 = reader.ReadByte(); q2 = tpoffset + b2; break;
                case 3: b2 = reader.ReadByte(); q2 = tpoffset - b2; break;
                case 4: w2 = reader.ReadUInt16(); q2 = tpoffset + w2; break;
                case 5: w2 = reader.ReadUInt16(); q2 = tpoffset - w2; break;
                case 6: w2 = reader.ReadUInt16(); q2 = w2; break;
                case 7: d2 = reader.ReadUInt32(); q2 = d2; break;
                default:
                    throw new InvalidDataException("Unknown high nibble encoding type");
            }

            if ((high & 0x8) != 0)
                q2 *= (ulong)pointerSize;

            addrMap[q1] = q2;
            idMap[q2] = q1;

            poffset = q2;
            pvid = q1;
        }

        return new AddrLibBin
        {
            _idToAddress = addrMap,
            _addressToId = idMap,
        };
    }
}
