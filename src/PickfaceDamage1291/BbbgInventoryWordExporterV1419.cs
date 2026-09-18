using System.Globalization;
using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace PickfaceDamage1291;

internal static class BbbgInventoryWordExporterV1419
{
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

        // V1.4.25: one approved V2 layout + exact OWNER-provided THE SUPRA logo.
        // Do not redesign, regenerate, substitute, or fall back to another logo/template.
        var templateBytes = BbbgInventoryTemplateV1424.GetBytes();
        var outputBytes = PopulateTemplateInMemory(templateBytes, reports);
        File.WriteAllBytes(path, outputBytes);

        AppLog.Info(
            "EXPORT_BBBG_INVENTORY_V1425_WORD_DONE",
            "Đã tạo Word BBBG Inventory đúng mẫu V2 canonical và đúng logo THE SUPRA do OWNER cung cấp.",
            new Dictionary<string, object?>
            {
                ["report_count"] = reports.Count,
                ["output_bytes"] = outputBytes.Length,
                ["template"] = "V2_CANONICAL_V1424",
                ["logo"] = "OWNER_THE_SUPRA_V1425",
                ["logo_sha256"] = BbbgOfficialLogoV1425.Sha256,
                ["shift_in_document"] = false,
                ["entry_date_sentence"] = reports.Count > 0 ? reports[0].CreatedAt.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : string.Empty
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
                       ?? throw new InvalidDataException("Mẫu BBBG V2 không có MainDocumentPart.");
            var body = main.Document.Body
                       ?? throw new InvalidDataException("Mẫu BBBG V2 không có nội dung Body.");

            ReplaceLogoWithOwnerSuppliedPng(main);
            ApplyEntryDateSentence(body, reports);

            var table = FindDataTable(body)
                        ?? throw new InvalidDataException("Không tìm thấy bảng dữ liệu BBBG 6 cột trong mẫu V2.");

            var rows = table.Elements<TableRow>().ToList();
            if (rows.Count < 2)
                throw new InvalidDataException("Mẫu BBBG V2 thiếu dòng mẫu dữ liệu.");

            var prototype = (TableRow)rows[1].CloneNode(true);
            foreach (var old in rows.Skip(1).ToList()) old.Remove();

            for (var i = 0; i < reports.Count; i++)
            {
                var report = reports[i];
                var row = (TableRow)prototype.CloneNode(true);
                EnsureCantSplit(row);
                var cells = row.Elements<TableCell>().ToList();
                if (cells.Count != 6)
                    throw new InvalidDataException("Dòng mẫu BBBG V2 không đúng 6 cột.");

                SetCellText(cells[0], (i + 1).ToString(CultureInfo.InvariantCulture), centered: true);
                SetCellText(cells[1], report.Sku ?? string.Empty, centered: false);
                SetCellText(cells[2], report.ProductName ?? string.Empty, centered: false);
                SetCellText(cells[3], report.Location ?? string.Empty, centered: false);
                SetCellText(cells[4], $"{FormatQuantity(report.Quantity)} - {report.BaseUnit?.Trim()}".TrimEnd(' ', '-'), centered: true);
                SetCellText(cells[5], DetectionTime(report).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), centered: true);
                table.Append(row);
            }

            main.Document.Save();
        }

        return memory.ToArray();
    }

    private static void ApplyEntryDateSentence(Body body, IReadOnlyList<DamageReport> reports)
    {
        if (reports.Count == 0)
            throw new InvalidDataException("BBBG không có dữ liệu để xác định ngày nhập thực tế.");

        var entryDates = reports
            .Select(x => x.CreatedAt.ToLocalTime().Date)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        if (entryDates.Count != 1)
            throw new InvalidDataException("Một file BBBG chỉ được chứa dữ liệu của đúng một ngày nhập thực tế.");

        var paragraph = body.Descendants<Paragraph>()
            .FirstOrDefault(x =>
            {
                var text = Normalize(x.InnerText);
                return text.Contains("HÔM NAY, NGÀY", StringComparison.Ordinal) &&
                       text.Contains("TẠI KHO THE SUPRA", StringComparison.Ordinal);
            })
            ?? throw new InvalidDataException("Mẫu BBBG V2 không tìm thấy dòng 'Hôm nay, ngày ... tại kho The Supra'.");

        var textValue = $"Hôm nay, ngày {entryDates[0]:dd/MM/yyyy}, vào lúc ............................ tại kho The Supra – Hưng yên, các bên gồm:";
        SetParagraphText(paragraph, textValue);
    }

    private static void SetParagraphText(Paragraph paragraph, string value)
    {
        var runProperties = paragraph.Descendants<RunProperties>()
            .FirstOrDefault()?.CloneNode(true) as RunProperties;

        foreach (var child in paragraph.ChildElements
                     .Where(x => x is not ParagraphProperties)
                     .ToList())
            child.Remove();

        var run = new Run();
        if (runProperties is not null) run.Append(runProperties);
        run.Append(new Text(value) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.Append(run);
    }

    private static void ReplaceLogoWithOwnerSuppliedPng(MainDocumentPart main)
    {
        var logoBytes = BbbgOfficialLogoV1425.GetBytes();
        var actualHash = Convert.ToHexString(SHA256.HashData(logoBytes)).ToLowerInvariant();
        if (!string.Equals(actualHash, BbbgOfficialLogoV1425.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Logo THE SUPRA nhúng trong phần mềm không khớp file OWNER đã cung cấp.");

        var blip = main.Document
            .Descendants<A.Blip>()
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Embed?.Value))
            ?? throw new InvalidDataException("Mẫu BBBG V2 không có vị trí ảnh logo.");

        var relationshipId = blip.Embed!.Value!;
        if (main.GetPartById(relationshipId) is not ImagePart imagePart)
            throw new InvalidDataException("Quan hệ ảnh logo trong mẫu BBBG V2 không hợp lệ.");

        using (var logoStream = new MemoryStream(logoBytes, writable: false))
        {
            imagePart.FeedData(logoStream);
        }

        // Preserve the official PNG's 160:171 proportions instead of stretching it to the old placeholder ratio.
        var drawing = blip.Ancestors<Drawing>().FirstOrDefault();
        var outerExtent = drawing?.Descendants<DW.Extent>().FirstOrDefault();
        if (drawing is not null && outerExtent?.Cy?.Value is long currentCy && currentCy > 0)
        {
            var currentCx = Math.Max(1L,
                (long)Math.Round(currentCy * (double)BbbgOfficialLogoV1425.PixelWidth / BbbgOfficialLogoV1425.PixelHeight));
            outerExtent.Cx = currentCx;

            foreach (var innerExtent in drawing.Descendants<A.Extents>())
            {
                innerExtent.Cx = currentCx;
                innerExtent.Cy = currentCy;
            }
        }
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

    private static void SetCellText(TableCell cell, string value, bool centered)
    {
        var paragraphs = cell.Elements<Paragraph>().ToList();
        var paragraph = paragraphs.FirstOrDefault() ?? cell.AppendChild(new Paragraph());
        foreach (var extra in paragraphs.Skip(1).ToList()) extra.Remove();

        var paragraphProperties = paragraph.GetFirstChild<ParagraphProperties>();
        if (paragraphProperties is null)
        {
            paragraphProperties = new ParagraphProperties();
            paragraph.PrependChild(paragraphProperties);
        }
        var justification = paragraphProperties.GetFirstChild<Justification>();
        if (justification is null)
        {
            justification = new Justification();
            paragraphProperties.Append(justification);
        }
        justification.Val = centered ? JustificationValues.Center : JustificationValues.Left;

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
