using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

if (args.Length != 1) throw new ArgumentException("Usage: BbbgTemplateProbe <template.docx|template-source.cs>");
var inputPath = Path.GetFullPath(args[0]);
var bytes = ReadTemplateBytes(inputPath);
var workDir = Path.Combine(Path.GetTempPath(), "pickface-bbbg-probe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workDir);
var path = Path.Combine(workDir, "template.docx");
File.WriteAllBytes(path, bytes);

Console.WriteLine($"TEMPLATE_SOURCE={inputPath}");
Console.WriteLine($"TEMPLATE_BYTES={bytes.Length}");
Console.WriteLine($"TEMPLATE_SHA256={Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}");
Console.WriteLine($"HEAD={Convert.ToHexString(bytes.Take(Math.Min(32, bytes.Length)).ToArray())}");

var openPath = path;
try
{
    using var stream = File.OpenRead(path);
    using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
    Console.WriteLine($"ZIP_OPEN=OK|ENTRIES={zip.Entries.Count}");
}
catch (Exception ex)
{
    Console.WriteLine($"ZIP_OPEN=FAILED|{ex.GetType().Name}|{ex.Message}");
    var repaired = SalvageLocalEntries(bytes);
    openPath = Path.Combine(workDir, "template.repaired.docx");
    File.WriteAllBytes(openPath, repaired);
    Console.WriteLine($"SALVAGE_BYTES={repaired.Length}");
    Console.WriteLine($"SALVAGE_SHA256={Convert.ToHexString(SHA256.HashData(repaired)).ToLowerInvariant()}");
}

using (var stream = File.OpenRead(openPath))
using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
{
    Console.WriteLine($"VALID_ZIP_ENTRIES={zip.Entries.Count}");
    foreach (var e in zip.Entries)
        Console.WriteLine($"ZIP_ENTRY={e.FullName}|LEN={e.Length}|CLEN={e.CompressedLength}");
}

using var doc = WordprocessingDocument.Open(openPath, false);
Console.WriteLine("OPENXML_OPEN=OK");
var main = doc.MainDocumentPart ?? throw new InvalidDataException("No MainDocumentPart");
var body = main.Document.Body ?? throw new InvalidDataException("No Body");
Console.WriteLine($"BODY_TEXT={body.InnerText}");
Console.WriteLine($"TABLE_COUNT={body.Elements<Table>().Count()}");
Console.WriteLine($"IMAGE_PART_COUNT={main.ImageParts.Count()}");
Console.WriteLine($"HEADER_COUNT={main.HeaderParts.Count()}");
Console.WriteLine($"FOOTER_COUNT={main.FooterParts.Count()}");

var dataTable = body.Elements<Table>().FirstOrDefault(table =>
{
    var first = table.Elements<TableRow>().FirstOrDefault();
    var cells = first?.Elements<TableCell>().ToList();
    return cells?.Count == 6 &&
           string.Equals(cells[0].InnerText.Trim(), "STT", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(cells[1].InnerText.Trim(), "SKU", StringComparison.OrdinalIgnoreCase);
});
if (dataTable is null) throw new InvalidDataException("Canonical BBBG template has no 6-column STT/SKU data table.");
if (dataTable.Elements<TableRow>().Count() < 2) throw new InvalidDataException("Canonical BBBG template has no prototype row.");
if (main.ImageParts.Count() < 1) throw new InvalidDataException("Canonical BBBG template is missing THE SUPRA logo image.");

foreach (var table in body.Elements<Table>())
{
    var rowCount = table.Elements<TableRow>().Count();
    var firstCells = table.Elements<TableRow>().FirstOrDefault()?.Elements<TableCell>().Select(x => x.InnerText).ToArray() ?? [];
    Console.WriteLine($"TABLE|ROWS={rowCount}|FIRST={string.Join(" || ", firstCells)}");
}
Console.WriteLine("PROBE_PASS=TRUE");

static byte[] ReadTemplateBytes(string path)
{
    if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return File.ReadAllBytes(path);

    var source = File.ReadAllText(path, Encoding.UTF8);
    const string marker = "private const string Base64 = \"\"\"";
    var start = source.IndexOf(marker, StringComparison.Ordinal);
    if (start < 0) throw new InvalidDataException("Template source Base64 marker not found.");
    start += marker.Length;
    var end = source.IndexOf("\"\"\";", start, StringComparison.Ordinal);
    if (end < 0) throw new InvalidDataException("Template source Base64 terminator not found.");
    var base64 = source[start..end];
    return Convert.FromBase64String(base64);
}

static byte[] SalvageLocalEntries(byte[] source)
{
    var entries = new List<(string Name, byte[] Data)>();
    var offset = 0;
    while (offset + 30 <= source.Length)
    {
        var signature = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset, 4));
        if (signature != 0x04034B50) break;

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset + 6, 2));
        var method = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset + 8, 2));
        var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + 18, 4));
        var uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + 22, 4));
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset + 26, 2));
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset + 28, 2));

        if ((flags & 0x0008) != 0)
            throw new InvalidDataException($"Entry at {offset} uses data descriptor; salvage parser refuses ambiguous boundaries.");

        var nameStart = offset + 30;
        var dataStart = checked(nameStart + nameLength + extraLength);
        var dataEnd = checked(dataStart + (int)compressedSize);
        if (dataEnd > source.Length)
            throw new InvalidDataException($"Entry at {offset} is truncated: data end {dataEnd} > {source.Length}.");

        var name = Encoding.UTF8.GetString(source.AsSpan(nameStart, nameLength));
        var compressed = source.AsSpan(dataStart, (int)compressedSize).ToArray();
        byte[] data;
        if (method == 0)
        {
            data = compressed;
        }
        else if (method == 8)
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream((int)Math.Min(uncompressedSize, int.MaxValue));
            deflate.CopyTo(output);
            data = output.ToArray();
        }
        else
        {
            throw new InvalidDataException($"Unsupported ZIP compression method {method} for {name}.");
        }

        if (uncompressedSize != 0 && data.Length != uncompressedSize)
            throw new InvalidDataException($"Recovered length mismatch for {name}: {data.Length} != {uncompressedSize}.");

        entries.Add((name, data));
        Console.WriteLine($"SALVAGED_ENTRY={name}|LEN={data.Length}|METHOD={method}");
        offset = dataEnd;
    }

    if (entries.Count == 0) throw new InvalidDataException("No local ZIP entries could be recovered.");
    if (!entries.Any(x => x.Name == "[Content_Types].xml") || !entries.Any(x => x.Name == "word/document.xml"))
        throw new InvalidDataException("Recovered package is missing required DOCX entries.");

    using var targetStream = new MemoryStream(Math.Max(source.Length + 4096, 16 * 1024));
    using (var target = new ZipArchive(targetStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var entry in entries)
        {
            var created = target.CreateEntry(entry.Name, CompressionLevel.Optimal);
            using var output = created.Open();
            output.Write(entry.Data, 0, entry.Data.Length);
        }
    }
    return targetStream.ToArray();
}
