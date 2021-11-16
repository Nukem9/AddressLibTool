namespace AddressLibTool;

public class SigDescriptor
{
    private readonly List<(byte Value, bool Wildcard)> _data;

    public SigDescriptor(string input)
    {
        // Expected format: 00 44 ? ? 66 ? 88 99
        _data = new List<(byte, bool)>();

        for (int i = 0; i < input.Length;)
        {
            if (input[i] == '?')
            {
                _data.Add((0, true));

                // Skip over the '?' and space
                i += 2;
            }
            else
            {
                var parsed = byte.Parse(input.Substring(i, Math.Min(2, input.Length - i)), System.Globalization.NumberStyles.HexNumber);
                _data.Add((parsed, false));

                // Skip over the byte and space
                i += 3;
            }
        }
    }

    public bool Matches(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < _data.Count)
            return false;

        for (int i = 0; i < _data.Count; i++)
        {
            if (!_data[i].Wildcard && _data[i].Value != bytes[i])
                return false;
        }

        return true;
    }

    public bool MatchAny(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            if (Matches(bytes.Slice(i)))
                return true;
        }

        return false;
    }
}
