using System.Globalization;
using System.IO.Compression;
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

        byte[] outputBytes;
        var templateBytes = ReadApprovedTemplateBytes();

        try
        {
            outputBytes = PopulateTemplateInMemory(templateBytes, reports);
        }
        catch (Exception first) when (first is FileFormatException or InvalidDataException)
        {
            AppLog.Warning(
                "EXPORT_BBBG_TEMPLATE_PACKAGE_REPAIR",
                "OpenXML không mở được template trực tiếp; thử đóng gói lại DOCX hoàn toàn trong RAM.",
                new Dictionary<string, object?>
                {
                    ["exception_type"] = first.GetType().FullName,
                    ["message"] = first.Message
                });

            try
            {
                var repairedBytes = RepairZipPackageInMemory(templateBytes);
                outputBytes = PopulateTemplateInMemory(repairedBytes, reports);
            }
            catch (Exception repairEx) when (repairEx is InvalidDataException or IOException or FileFormatException)
            {
                AppLog.Warning(
                    "EXPORT_BBBG_TEMPLATE_FALLBACK",
                    "Template BBBG không dùng được sau khi sửa package; tạo DOCX sạch hoàn toàn trong RAM.",
                    new Dictionary<string, object?>
                    {
                        ["first_exception_type"] = first.GetType().FullName,
                        ["first_exception_message"] = first.Message,
                        ["repair_exception_type"] = repairEx.GetType().FullName,
                        ["repair_exception_message"] = repairEx.Message,
                        ["report_count"] = reports.Count
                    });

                outputBytes = CreateFallbackDocumentBytes(reports);
            }
        }

        File.WriteAllBytes(path, outputBytes);

        AppLog.Info(
            "EXPORT_BBBG_INVENTORY_V1419_WORD_DONE",
            "Đã tạo Word BBBG Inventory; OpenXML xử lý trong RAM và chỉ ghi file hoàn chỉnh một lần.",
            new Dictionary<string, object?>
            {
                ["report_count"] = reports.Count,
                ["output_bytes"] = outputBytes.Length,
                ["shift_in_document"] = false
            });
    }

    private static byte[] PopulateTemplateInMemory(byte[] templateBytes, IReadOnlyList<DamageReport> reports)
    {
        using var memory = new MemoryStream(Math.Max(templateBytes.Length + 64 * 1024, 128 * 1024));
        memory.Write(templateBytes, 0, templateBytes.Length);
        memory.Position = 0;

        using (var document = WordprocessingDocument.Open(memory, true))
        {
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
        }

        return memory.ToArray();
    }

    private static byte[] RepairZipPackageInMemory(byte[] sourceBytes)
    {
        using var sourceStream = new MemoryStream(sourceBytes, writable: false);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);
        using var targetStream = new MemoryStream(Math.Max(sourceBytes.Length + 4096, 16 * 1024));

        using (var target = new ZipArchive(targetStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var targetEntry = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

                using var input = entry.Open();
                using var output = targetEntry.Open();
                input.CopyTo(output);
            }
        }

        return targetStream.ToArray();
    }

    private static byte[] CreateFallbackDocumentBytes(IReadOnlyList<DamageReport> reports)
    {
        using var memory = new MemoryStream(128 * 1024);

        using (var document = WordprocessingDocument.Create(memory, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document();
            var body = new Body();
            main.Document.Append(body);

            body.Append(CreateParagraph("BBBG INVENTORY PICKFACE 1291", bold: true, centered: true, fontSizeHalfPoints: "24"));
            body.Append(CreateParagraph(string.Empty, bold: false, centered: false, fontSizeHalfPoints: "20"));

            var table = new Table();
            table.AppendChild(new TableProperties(
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4U },
                    new LeftBorder { Val = BorderValues.Single, Size = 4U },
                    new BottomBorder { Val = BorderValues.Single, Size = 4U },
                    new RightBorder { Val = BorderValues.Single, Size = 4U },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4U })));

            table.Append(CreateFallbackRow(new[]
            {
                "STT", "SKU", "TÊN SẢN PHẨM", "VỊ TRÍ", "SỐ LƯỢNG", "THỜI GIAN PHÁT HIỆN"
            }, header: true));

            for (var i = 0; i < reports.Count; i++)
            {
                var report = reports[i];
                table.Append(CreateFallbackRow(new[]
                {
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    report.Sku ?? string.Empty,
                    report.ProductName ?? string.Empty,
                    report.Location ?? string.Empty,
                    $"{FormatQuantity(report.Quantity)} - {report.BaseUnit?.Trim()}".TrimEnd(' ', '-'),
                    DetectionTime(report).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                }, header: false));
            }

            body.Append(table);
            main.Document.Save();
        }

        var bytes = memory.ToArray();
        AppLog.Info(
            "EXPORT_BBBG_FALLBACK_WORD_DONE",
            "Đã tạo DOCX sạch thay thế hoàn toàn trong RAM do template package lỗi.",
            new Dictionary<string, object?>
            {
                ["report_count"] = reports.Count,
                ["columns"] = 6,
                ["output_bytes"] = bytes.Length
            });
        return bytes;
    }

    private static byte[] ReadApprovedTemplateBytes()
    {
        var assembly = typeof(BbbgInventoryWordExporterV1419).Assembly;
        using var source = assembly.GetManifestResourceStream(TemplateResourceName)
                           ?? throw new FileNotFoundException($"Không tìm thấy template nhúng: {TemplateResourceName}");
        using var memory = new MemoryStream();
        source.CopyTo(memory);
        return memory.ToArray();
    }

    private static TableRow CreateFallbackRow(IReadOnlyList<string> values, bool header)
    {
        var row = new TableRow();
        EnsureCantSplit(row);
        foreach (var value in values)
        {
            var paragraph = CreateParagraph(value, bold: header, centered: header, fontSizeHalfPoints: "20");
            var cell = new TableCell(paragraph);
            cell.AppendChild(new TableCellProperties(
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }));
            row.Append(cell);
        }
        return row;
    }

    private static Paragraph CreateParagraph(string value, bool bold, bool centered, string fontSizeHalfPoints)
    {
        var paragraphProperties = new ParagraphProperties();
        if (centered) paragraphProperties.Append(new Justification { Val = JustificationValues.Center });

        var runProperties = new RunProperties(
            new RunFonts { Ascii = "Arial", HighAnsi = "Arial", EastAsia = "Arial" },
            new FontSize { Val = fontSizeHalfPoints });
        if (bold) runProperties.Append(new Bold());

        var run = new Run(runProperties, new Text(value ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve });
        return new Paragraph(paragraphProperties, run);
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
