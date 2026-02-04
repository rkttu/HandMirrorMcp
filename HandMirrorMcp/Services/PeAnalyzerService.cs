using System.Runtime.InteropServices;
using System.Text;

namespace HandMirrorMcp.Services;

/// <summary>
/// Service for analyzing Export/Import tables of Windows PE (Portable Executable) files
/// </summary>
public sealed class PeAnalyzerService
{
    /// <summary>
    /// Check if PE analysis is supported on current OS
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Analyzes a PE file.
    /// </summary>
    public static PeAnalysisResult? AnalyzePeFile(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);

            var result = new PeAnalysisResult
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath)
            };

            // DOS Header
            if (reader.ReadUInt16() != 0x5A4D) // "MZ"
            {
                return null; // Not a valid PE file
            }

            stream.Seek(0x3C, SeekOrigin.Begin);
            var peHeaderOffset = reader.ReadInt32();

            // PE Signature
            stream.Seek(peHeaderOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550) // "PE\0\0"
            {
                return null;
            }

            // COFF Header
            var machine = reader.ReadUInt16();
            result.Machine = GetMachineType(machine);

            var numberOfSections = reader.ReadUInt16();
            reader.ReadUInt32(); // TimeDateStamp
            reader.ReadUInt32(); // PointerToSymbolTable
            reader.ReadUInt32(); // NumberOfSymbols
            var sizeOfOptionalHeader = reader.ReadUInt16();
            var characteristics = reader.ReadUInt16();

            result.IsDll = (characteristics & 0x2000) != 0; // IMAGE_FILE_DLL

            if (sizeOfOptionalHeader == 0)
            {
                return result;
            }

            // Optional Header
            var optionalHeaderStart = stream.Position;
            var magic = reader.ReadUInt16();
            var is64Bit = magic == 0x20B; // PE32+
            result.Is64Bit = is64Bit;

            // Skip to Data Directories
            if (is64Bit)
            {
                stream.Seek(optionalHeaderStart + 112, SeekOrigin.Begin); // PE32+ data directory offset
            }
            else
            {
                stream.Seek(optionalHeaderStart + 96, SeekOrigin.Begin); // PE32 data directory offset
            }

            // Read Data Directories
            var exportRva = reader.ReadUInt32();
            var exportSize = reader.ReadUInt32();
            var importRva = reader.ReadUInt32();
            var importSize = reader.ReadUInt32();

            // Read Section Headers
            stream.Seek(optionalHeaderStart + sizeOfOptionalHeader, SeekOrigin.Begin);
            var sections = new List<SectionHeader>();

            for (int i = 0; i < numberOfSections; i++)
            {
                var nameBytes = reader.ReadBytes(8);
                var name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                var virtualSize = reader.ReadUInt32();
                var virtualAddress = reader.ReadUInt32();
                var sizeOfRawData = reader.ReadUInt32();
                var pointerToRawData = reader.ReadUInt32();
                reader.ReadBytes(16); // Skip remaining fields

                sections.Add(new SectionHeader
                {
                    Name = name,
                    VirtualAddress = virtualAddress,
                    VirtualSize = virtualSize,
                    PointerToRawData = pointerToRawData,
                    SizeOfRawData = sizeOfRawData
                });
            }

            // Parse Export Table
            if (exportRva != 0 && exportSize != 0)
            {
                result.Exports = ParseExportTable(reader, stream, exportRva, sections);
            }

            // Parse Import Table
            if (importRva != 0 && importSize != 0)
            {
                result.Imports = ParseImportTable(reader, stream, importRva, sections, is64Bit);
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static List<ExportedFunction> ParseExportTable(
        BinaryReader reader, Stream stream, uint exportRva, List<SectionHeader> sections)
    {
        var exports = new List<ExportedFunction>();

        var fileOffset = RvaToFileOffset(exportRva, sections);
        if (fileOffset == 0) return exports;

        stream.Seek(fileOffset, SeekOrigin.Begin);

        // Export Directory Table
        reader.ReadUInt32(); // Characteristics
        reader.ReadUInt32(); // TimeDateStamp
        reader.ReadUInt16(); // MajorVersion
        reader.ReadUInt16(); // MinorVersion
        var nameRva = reader.ReadUInt32();
        var ordinalBase = reader.ReadUInt32();
        var numberOfFunctions = reader.ReadUInt32();
        var numberOfNames = reader.ReadUInt32();
        var addressOfFunctionsRva = reader.ReadUInt32();
        var addressOfNamesRva = reader.ReadUInt32();
        var addressOfNameOrdinalsRva = reader.ReadUInt32();

        // Read function addresses
        var functionAddresses = new uint[numberOfFunctions];
        var functionsOffset = RvaToFileOffset(addressOfFunctionsRva, sections);
        if (functionsOffset != 0)
        {
            stream.Seek(functionsOffset, SeekOrigin.Begin);
            for (int i = 0; i < numberOfFunctions; i++)
            {
                functionAddresses[i] = reader.ReadUInt32();
            }
        }

        // Read name RVAs
        var nameRvas = new uint[numberOfNames];
        var namesOffset = RvaToFileOffset(addressOfNamesRva, sections);
        if (namesOffset != 0)
        {
            stream.Seek(namesOffset, SeekOrigin.Begin);
            for (int i = 0; i < numberOfNames; i++)
            {
                nameRvas[i] = reader.ReadUInt32();
            }
        }

        // Read name ordinals
        var nameOrdinals = new ushort[numberOfNames];
        var ordinalsOffset = RvaToFileOffset(addressOfNameOrdinalsRva, sections);
        if (ordinalsOffset != 0)
        {
            stream.Seek(ordinalsOffset, SeekOrigin.Begin);
            for (int i = 0; i < numberOfNames; i++)
            {
                nameOrdinals[i] = reader.ReadUInt16();
            }
        }

        // Build export list with names
        var namedExports = new Dictionary<int, string>();
        for (int i = 0; i < numberOfNames; i++)
        {
            var name = ReadNullTerminatedString(reader, stream, nameRvas[i], sections);
            if (!string.IsNullOrEmpty(name))
            {
                namedExports[nameOrdinals[i]] = name;
            }
        }

        // Create export entries
        for (int i = 0; i < numberOfFunctions; i++)
        {
            if (functionAddresses[i] == 0)
                continue;

            var ordinal = (int)(ordinalBase + i);
            namedExports.TryGetValue(i, out var name);

            exports.Add(new ExportedFunction
            {
                Ordinal = ordinal,
                Name = name,
                Rva = functionAddresses[i]
            });
        }

        return exports;
    }

    private static List<ImportedModule> ParseImportTable(
        BinaryReader reader, Stream stream, uint importRva, List<SectionHeader> sections, bool is64Bit)
    {
        var imports = new List<ImportedModule>();

        var fileOffset = RvaToFileOffset(importRva, sections);
        if (fileOffset == 0) return imports;

        stream.Seek(fileOffset, SeekOrigin.Begin);

        while (true)
        {
            var importLookupTableRva = reader.ReadUInt32();
            reader.ReadUInt32(); // TimeDateStamp
            reader.ReadUInt32(); // ForwarderChain
            var nameRva = reader.ReadUInt32();
            var importAddressTableRva = reader.ReadUInt32();

            // End of import directory
            if (importLookupTableRva == 0 && nameRva == 0)
                break;

            var moduleName = ReadNullTerminatedString(reader, stream, nameRva, sections);
            if (string.IsNullOrEmpty(moduleName))
                continue;

            var module = new ImportedModule { Name = moduleName };

            // Parse Import Lookup Table (or IAT if ILT is 0)
            var lookupRva = importLookupTableRva != 0 ? importLookupTableRva : importAddressTableRva;
            var lookupOffset = RvaToFileOffset(lookupRva, sections);

            if (lookupOffset != 0)
            {
                var currentPos = stream.Position;
                stream.Seek(lookupOffset, SeekOrigin.Begin);

                while (true)
                {
                    ulong entry;
                    if (is64Bit)
                    {
                        entry = reader.ReadUInt64();
                    }
                    else
                    {
                        entry = reader.ReadUInt32();
                    }

                    if (entry == 0)
                        break;

                    var isOrdinal = is64Bit
                        ? (entry & 0x8000000000000000) != 0
                        : (entry & 0x80000000) != 0;

                    if (isOrdinal)
                    {
                        var ordinal = (int)(entry & 0xFFFF);
                        module.Functions.Add(new ImportedFunction
                        {
                            Ordinal = ordinal,
                            IsOrdinal = true
                        });
                    }
                    else
                    {
                        var hintNameRva = (uint)(entry & 0x7FFFFFFF);
                        var hintNameOffset = RvaToFileOffset(hintNameRva, sections);

                        if (hintNameOffset != 0)
                        {
                            var lookupPos = stream.Position;
                            stream.Seek(hintNameOffset, SeekOrigin.Begin);

                            var hint = reader.ReadUInt16();
                            var funcName = ReadNullTerminatedStringDirect(reader);

                            module.Functions.Add(new ImportedFunction
                            {
                                Name = funcName,
                                Hint = hint,
                                IsOrdinal = false
                            });

                            stream.Seek(lookupPos, SeekOrigin.Begin);
                        }
                    }
                }

                stream.Seek(currentPos, SeekOrigin.Begin);
            }

            imports.Add(module);
        }

        return imports;
    }

    private static uint RvaToFileOffset(uint rva, List<SectionHeader> sections)
    {
        foreach (var section in sections)
        {
            if (rva >= section.VirtualAddress &&
                rva < section.VirtualAddress + section.VirtualSize)
            {
                return rva - section.VirtualAddress + section.PointerToRawData;
            }
        }
        return 0;
    }

    private static string ReadNullTerminatedString(
        BinaryReader reader, Stream stream, uint rva, List<SectionHeader> sections)
    {
        var offset = RvaToFileOffset(rva, sections);
        if (offset == 0) return "";

        var currentPos = stream.Position;
        stream.Seek(offset, SeekOrigin.Begin);

        var result = ReadNullTerminatedStringDirect(reader);

        stream.Seek(currentPos, SeekOrigin.Begin);
        return result;
    }

    private static string ReadNullTerminatedStringDirect(BinaryReader reader)
    {
        var bytes = new List<byte>();
        byte b;
        while ((b = reader.ReadByte()) != 0 && bytes.Count < 512)
        {
            bytes.Add(b);
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static string GetMachineType(ushort machine)
    {
        return machine switch
        {
            0x014c => "x86 (I386)",
            0x0200 => "IA64",
            0x8664 => "x64 (AMD64)",
            0x01c0 => "ARM",
            0x01c4 => "ARMv7 Thumb-2",
            0xaa64 => "ARM64",
            _ => $"Unknown (0x{machine:X4})"
        };
    }

    private sealed class SectionHeader
    {
        public required string Name { get; init; }
        public uint VirtualAddress { get; init; }
        public uint VirtualSize { get; init; }
        public uint PointerToRawData { get; init; }
        public uint SizeOfRawData { get; init; }
    }
}

/// <summary>
/// PE analysis result
/// </summary>
public sealed class PeAnalysisResult
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public string Machine { get; set; } = "";
    public bool Is64Bit { get; set; }
    public bool IsDll { get; set; }
    public List<ExportedFunction> Exports { get; set; } = [];
    public List<ImportedModule> Imports { get; set; } = [];
}

/// <summary>
/// Exported function
/// </summary>
public sealed class ExportedFunction
{
    public int Ordinal { get; init; }
    public string? Name { get; init; }
    public uint Rva { get; init; }

    public override string ToString()
    {
        return Name != null ? $"{Ordinal}: {Name}" : $"{Ordinal}: (ordinal only)";
    }
}

/// <summary>
/// Imported module
/// </summary>
public sealed class ImportedModule
{
    public required string Name { get; init; }
    public List<ImportedFunction> Functions { get; } = [];
}

/// <summary>
/// Imported function
/// </summary>
public sealed class ImportedFunction
{
    public string? Name { get; init; }
    public int Ordinal { get; init; }
    public int Hint { get; init; }
    public bool IsOrdinal { get; init; }

    public override string ToString()
    {
        return IsOrdinal ? $"@{Ordinal}" : Name ?? "";
    }
}

