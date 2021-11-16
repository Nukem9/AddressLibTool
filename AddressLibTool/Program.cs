namespace AddressLibTool;

public class Program
{
    private static readonly string GameExecutablePath = @"C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition\SkyrimSE.exe";
    private static readonly string AddressLibraryPath = @"C:\Users\Administrator\Desktop\All in one-32444-2-1582543914\SKSE\Plugins\version-1-5-97-0.bin";
    private static readonly string SourceCodeDirectoryPath = @"C:\Users\Administrator\source\repos\CommonLibSSE";

    public enum SourceCodeIdType
    {
        Id,
        Offset,
    }

    public static void Main(string[] args)
    {
        string finalizedFile = Path.Combine(Environment.CurrentDirectory, "finalized.csv");
        string sigGenFile = Path.Combine(Environment.CurrentDirectory, "generation.txt");

        Console.WriteLine("Loading address library and scanning source code for markers...");

        var inputAddressLib = AddrLibBin.LoadFile(AddressLibraryPath);
        var inputOffsets = ScanSourceFilesForOffsets(SourceCodeDirectoryPath);
        var combinedInputOffsets = new List<(ulong Id, ulong Address)>();
        var validSignatures = new Dictionary<ulong, string>();

        foreach (var entry in inputOffsets)
        {
            if (entry.Item1 == SourceCodeIdType.Id)
                combinedInputOffsets.Add((entry.Item2, inputAddressLib.GetAddressFromId(entry.Item2)));
            else if (entry.Item1 == SourceCodeIdType.Offset)
                combinedInputOffsets.Add((entry.Item2, 0));
        }

        Console.WriteLine("Use signature generation file? (y/n): ");
        string? result = Console.ReadLine();

        if (result == "Y" || result == "y" || result == "yes")
        {
            ProduceSignatureGeneratorInputFile(sigGenFile, combinedInputOffsets);

            Console.WriteLine($"Signature generation file written. Press enter when the contents are updated. Expected file path: '{sigGenFile}'");
            Console.ReadLine();

            // Determine which signatures are valid
            if (File.Exists(sigGenFile))
            {
                ReadOnlySpan<byte> allFileData = File.ReadAllBytes(GameExecutablePath);
                var signatures = LoadGeneratedSignatureFile(sigGenFile);

                foreach (var (address, str) in signatures)
                {
                    Console.WriteLine($"Scanning for '{str}'");
                    var descriptor = new SigDescriptor(str);

                    if (descriptor.MatchAny(allFileData))
                        validSignatures.Add(address, str);
                }
            }
        }

        // Create a new file with all of the compiled data
        var finalizedLines = new List<string>();

        foreach (var offset in combinedInputOffsets.OrderBy(x => x.Address))
        {
            finalizedLines.Add($"{offset.Id},0x{offset.Address:X},0x{offset.Address + 0x140000000:X},{GetBytePattern()},");

            string GetBytePattern()
            {
                if (!validSignatures.TryGetValue(offset.Address, out var signature))
                    return string.Empty;

                return signature;
            }
        }

        File.WriteAllLines(finalizedFile, finalizedLines);

        // Replace the old offsets in the source code
        Console.WriteLine($"Finalized CSV file written. Update the CSV, then press enter to update the source code. Expected file path: '{finalizedFile}'");
        Console.ReadLine();

        RemapOffsetsFromCsv(SourceCodeDirectoryPath, finalizedFile);
    }

    private static void RemapOffsetsFromCsv(string directoryPath, string finalizedCsvPath)
    {
        //
        // Build a lookup table from the CSV file, then remap it all.
        //
        // OldRELID,OldRVA,OldAddress,Signature,NewAddress
        // 11045,FCFE0,1400FCFE0,40 53 48 83 EC 20 83 3D ? ? ? ? ? 74,140106EC0
        //
        var finalizedCsvData = File.ReadAllLines(finalizedCsvPath);

        var idMap = new Dictionary<ulong, (ulong NewId, ulong NewOffset)>();
        var offsetMap = new Dictionary<ulong, ulong>();

        foreach (string line in finalizedCsvData)
        {
            string[] tokens = line.Split(',');

            if (tokens.Length != 5)
                throw new FormatException("Unknown CSV format. Expected 'OldRELID,OldRVA,OldAddress,Signature,NewAddress'.");

            // Set invalid offsets to zero
            if (tokens[4].StartsWith("//"))
                tokens[4] = "0";

            ulong oldRelId = ulong.Parse(tokens[0], System.Globalization.NumberStyles.Integer);
            ulong oldRva = ulong.Parse(tokens[1], System.Globalization.NumberStyles.HexNumber);
            ulong newAddress = ulong.Parse(tokens[4], System.Globalization.NumberStyles.HexNumber);

            newAddress = (newAddress > 0) ? (newAddress - 0x140000000) : 0;

            idMap.TryAdd(oldRelId, (ulong.MaxValue, newAddress));
            offsetMap.TryAdd(oldRva, newAddress);
        }

        ReplaceOffsetsInSourceFiles(directoryPath, idMap, offsetMap, true);
    }

    private static List<(SourceCodeIdType, ulong)> ScanSourceFilesForOffsets(string directoryPath)
    {
        var sourceMapping = new List<(SourceCodeIdType, ulong)>();
        var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);

        // Scan for all source code files in this directory (.cpp, .h)
        foreach (var file in files)
        {
            if (FilterFile(file))
                continue;

            // Find all instances of REL::ID or REL::Offset
            var linesFromFile = File.ReadAllLines(file);

            foreach (var line in linesFromFile)
            {
                if (line.Contains("REL::ID"))
                {
                    //
                    // Match lines such as:
                    //
                    // 'REL::Relocation<const Setting*> fSafeZoneXWide{ REL::ID(512509) };' => (SourceCodeIdType.Id, 512509)
                    // 'inline constexpr REL::ID AddMessage(static_cast<std::uint64_t>(13530));' => (SourceCodeIdType.Id, 13530)
                    //
                    if (!Utility.TryExtractNumberInParenthesis(line, out ulong id))
                        continue;

                    sourceMapping.Add((SourceCodeIdType.Id, id));
                }
                else if (line.Contains("REL::Offset"))
                {
                    //
                    // Match lines such as:
                    //
                    // `REL::Relocation<func_t> func{ REL::Offset(0xC4F2E0) };` => (SourceCodeIdType.Offset, 0xC4F2E0)
                    // `inline constexpr REL::Offset Vtbl(static_cast<std::uint64_t>(0x174B948));` => (SourceCodeIdType.Offset, 0x174B948)
                    //
                    if (!Utility.TryExtractNumberInParenthesis(line, out ulong offset))
                        continue;

                    sourceMapping.Add((SourceCodeIdType.Offset, offset));
                }
            }
        }

        return sourceMapping;
    }

    private static void ReplaceOffsetsInSourceFiles(string directoryPath, Dictionary<ulong, (ulong NewId, ulong NewOffset)> idMap, Dictionary<ulong, ulong> offsetMap, bool forceReplaceIds)
    {
        var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);

        // Scan for all source code files in this directory (.cpp, .h)
        foreach (var file in files)
        {
            if (FilterFile(file))
                continue;

            // Find all instances of REL::ID or REL::Offset
            var linesFromFile = File.ReadAllLines(file);
            var newLines = new List<string>(linesFromFile.Length);

            for (int i = 0; i < linesFromFile.Length; i++)
            {
                string line = linesFromFile[i];

                if (line.Contains("REL::ID") && Utility.TryExtractNumberInParenthesis(line, out ulong id))
                {
                    if (forceReplaceIds)
                    {
                        // Convert IDs to offsets in the code itself
                        line = line.Replace("REL::ID", "REL::Offset");
                        line = line.Replace($"{id}", $"0x{idMap[id].NewOffset:X}");
                    }
                    else
                    {
                        // String replace the old ID with the new ID. Yes, this is lazy but you'd have to intentionally
                        // break it.
                        line = line.Replace($"{id}", $"{idMap[id].NewId}");
                    }
                }
                else if (line.Contains("REL::Offset") && Utility.TryExtractNumberInParenthesis(line, out ulong offset))
                {
                    throw new NotImplementedException();

                    // line = line.Replace($"{offset}", $"{offsetMap[offset]}");
                }

                newLines.Add(line);
            }

            File.WriteAllLines(file, newLines);
        }
    }

    private static void ProduceSignatureGeneratorInputFile(string path, List<(ulong Id, ulong Address)> entries)
    {
        File.WriteAllLines(path, entries.Select(x => $"0x{x.Address:X}"));
    }

    private static Dictionary<ulong, string> LoadGeneratedSignatureFile(string path)
    {
        var lines = File.ReadAllLines(path);
        var dict = new Dictionary<ulong, string>();

        foreach (string line in lines)
        {
            //
            // Match lines in the format of:
            //
            // 0xF7210: Unable to find a valid signature for given length
            // 0xF9E90: 40 57 41 54 41
            // 0xFCFE0: 40 53 48 83 EC 20 83 3D ? ? ? ? ? 74
            //
            int splitterIndex = line.IndexOf(':');

            if (splitterIndex == -1)
                throw new Exception("Unknown input format. A ':' was not found.");

            string addrPart = line.Substring(0, splitterIndex).Trim();
            var parsedAddr = ulong.Parse(addrPart.Substring(2), System.Globalization.NumberStyles.HexNumber);

            string signaturePart = line.Substring(splitterIndex + 1).Trim();

            if (signaturePart.Equals("Unable to find a valid signature for given length", StringComparison.InvariantCultureIgnoreCase))
                continue;

            dict.Add(parsedAddr, signaturePart);
        }

        return dict;
    }

    private static bool FilterFile(string path)
    {
        string ext = Path.GetExtension(path).ToLower();

        if (ext != ".h" && ext != ".cpp")
            return true;

        if (path.Contains("Offsets_NiRTTI.h") || path.Contains("Offsets_RTTI.h"))
            return true;

        return false;
    }
}
