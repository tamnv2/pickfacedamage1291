using System.IO.Compression;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;

if (args.Length != 1) throw new ArgumentException("Usage: BbbgTemplateProbe <template.docx>");
var path = Path.GetFullPath(args[0]);
var bytes = File.ReadAllBytes(path);
Console.WriteLine($"TEMPLATE_PATH={path}");
Console.WriteLine($"TEMPLATE_BYTES={bytes.Length}");
Console.WriteLine($"TEMPLATE_SHA256={Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}");
Console.WriteLine($"HEAD={Convert.ToHexString(bytes.Take(Math.Min(32, bytes.Length)).ToArray())}");

try
{
    using var stream = File.OpenRead(path);
    using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
    Console.WriteLine($"ZIP_ENTRIES={zip.Entries.Count}");
    foreach (var e in zip.Entries)
    {
        Console.WriteLine($"ZIP_ENTRY={e.FullName}|LEN={e.Length}|CLEN={e.CompressedLength}");
        if (e.FullName is "[Content_Types].xml" or "_rels/.rels" or "word/_rels/document.xml.rels")
        {
            using var sr = new StreamReader(e.Open());
            Console.WriteLine($"---{e.FullName}---");
            Console.WriteLine(await sr.ReadToEndAsync());
            Console.WriteLine($"---END {e.FullName}---");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine("ZIP_OPEN_FAILED");
    Console.WriteLine(ex.ToString());
}

try
{
    using var doc = WordprocessingDocument.Open(path, false);
    Console.WriteLine("OPENXML_OPEN=OK");
    Console.WriteLine($"MAIN_PART={(doc.MainDocumentPart is null ? "NULL" : "OK")}");
    Console.WriteLine($"BODY_TEXT={doc.MainDocumentPart?.Document?.Body?.InnerText}");
    var tables = doc.MainDocumentPart?.Document?.Body?.Elements<DocumentFormat.OpenXml.Wordprocessing.Table>().Count() ?? 0;
    Console.WriteLine($"TABLE_COUNT={tables}");
}
catch (Exception ex)
{
    Console.WriteLine("OPENXML_OPEN=FAILED");
    Console.WriteLine(ex.ToString());
    Environment.ExitCode = 7;
}
