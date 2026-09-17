using System.Globalization;
using System.Reflection;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PickfaceDamage1291;

internal static class BbbgInventoryWordExporterV1419
{
    private const string TemplateResourceName = "PickfaceDamage1291.Assets.BbbgInventoryTemplateV2Size10.docx";

    public static void Export(string path, IReadOnlyList<DamageReport> sourceReports)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(sourceReports);

        var reports = sourceReports
            .OrderBy(x => x.OccurredDate)
            .ThenBy(x => x.Hour)
            .ThenBy(x => x.Minute)
            .ThenBy(x => x.CreatedAt)
            .ToList();

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        CopyApprovedTemplate(path);
        using var document = WordprocessingDocument.Open(path, true);
        var main = document.MainDocumentPart
                   ?? throw new InvalidDataException("Template BBBG không có MainDocumentPart.");
        var body = main.Document.Body
                   ?? throw new InvalidDataException("Template BBBG không có nội dung Body.");
        var table = FindDataTable(body)
                    ?? throw new InvalidDataException("Không tìm thấy bảng dữ liệu BBBG 6 cột trong template đã duyệt.");

        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count < 2)
            throw new InvalidDataException("Template BBBG thiếu dòng mẫu dữ liệu.");

        var prototype = (TableRow)rows[1].CloneNode(true);
        foreach (var old in rows.Skip(1).ToList()) old.Remove();

        for (var i = 0; i < reports.Count; i++)
        {
            var report = reports[i];
            var row = (TableRow)prototype.CloneNode(true);
            EnsureCantSplit(row);
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count != 6)
                throw new InvalidDataException("Dòng mẫu BBBG không đúng 6 cột.");

            SetCellText(cells[0], (i + 1).ToString(CultureInfo.InvariantCulture));
            SetCellText(cells[1], report.Sku ?? string.Empty);
            SetCellText(cells[2], report.ProductName ?? string.Empty);
            SetCellText(cells[3], report.Location ?? string.Empty);
            SetCellText(cells[4], $"{FormatQuantity(report.Quantity)} - {report.BaseUnit?.Trim()}".TrimEnd(' ', '-'));
            SetCellText(cells[5], DetectionTime(report).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
            table.Append(row);
        }

        main.Document.Save();
        AppLog.Info("EXPORT_BBBG_INVENTORY_V1419_WORD_DONE", "Đã tạo Word BBBG Inventory theo template V2 đã duyệt.",
            new Dictionary<string, object?>
            {
                ["report_count"] = reports.Count,
                ["template"] = "V2_SIZE10",
                ["shift_in_document"] = false
            });
    }

    private static void CopyApprovedTemplate(string path)
    {
        var assembly = typeof(BbbgInventoryWordExporterV1419).Assembly;
        using var source = assembly.GetManifestResourceStream(TemplateResourceName)
                           ?? throw new FileNotFoundException($"Không tìm thấy template nhúng: {TemplateResourceName}");
        using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }

    private static Table? FindDataTable(Body body)
    {
        foreach (var table in body.Elements<Table>())
        {
            var firstRow = table.Elements<TableRow>().FirstOrDefault();
            if (firstRow is null) continue;
            var cells = firstRow.Elements<TableCell>().ToList();
            if (cells.Count != 6) continue;
            var first = Normalize(cells[0].InnerText);
            var second = Normalize(cells[1].InnerText);
            var third = Normalize(cells[2].InnerText);
            if (first == "STT" && second == "SKU" && third.Contains("TÊN SẢN PHẨM", StringComparison.Ordinal))
                return table;
        }
        return null;
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim().ToUpperInvariant();

    private static void EnsureCantSplit(TableRow row)
    {
        var props = row.GetFirstChild<TableRowProperties>();
        if (props is null)
        {
            props = new TableRowProperties();
            row.PrependChild(props);
        }
        if (props.GetFirstChild<CantSplit>() is null) props.Append(new CantSplit());
    }

    private static void SetCellText(TableCell cell, string value)
    {
        var paragraphs = cell.Elements<Paragraph>().ToList();
        var paragraph = paragraphs.FirstOrDefault() ?? cell.AppendChild(new Paragraph());
        foreach (var extra in paragraphs.Skip(1).ToList()) extra.Remove();

        var runProperties = paragraph.Descendants<RunProperties>()
            .LastOrDefault()?.CloneNode(true) as RunProperties;
        foreach (var run in paragraph.Elements<Run>().ToList()) run.Remove();

        var newRun = new Run();
        if (runProperties is not null) newRun.Append(runProperties);
        newRun.Append(new Text(value ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.Append(newRun);
    }

    private static string FormatQuantity(decimal quantity)
    {
        var truncated = decimal.Truncate(quantity);
        return quantity == truncated
            ? truncated.ToString("0", CultureInfo.InvariantCulture)
            : quantity.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static DateTime DetectionTime(DamageReport report)
    {
        var date = report.OccurredDate.Date;
        return new DateTime(date.Year, date.Month, date.Day,
            Math.Clamp(report.Hour, 0, 23), Math.Clamp(report.Minute, 0, 59), 0,
            DateTimeKind.Unspecified);
    }
}
