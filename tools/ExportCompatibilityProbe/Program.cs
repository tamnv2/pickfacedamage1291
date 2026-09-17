using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;

var temp = Path.Combine(Path.GetTempPath(), $"pickface_export_probe_{Guid.NewGuid():N}.xlsx");
try
{
    using (var workbook = new XLWorkbook())
    {
        var sheet = workbook.AddWorksheet("Probe");
        sheet.Cell("A1").Value = "SKU";
        sheet.Cell("B1").Value = "10000001";
        sheet.Cell("A2").Value = "Qty";
        sheet.Cell("B2").Value = 1;
        workbook.SaveAs(temp);
    }

    using var document = SpreadsheetDocument.Open(temp, false);
    if (document.WorkbookPart?.Workbook is null)
        throw new InvalidDataException("ClosedXML smoke probe created an invalid XLSX package.");

    Console.WriteLine("EXPORT COMPATIBILITY PROBE PASS");
    Console.WriteLine($"XLSX bytes: {new FileInfo(temp).Length}");
}
finally
{
    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
}
