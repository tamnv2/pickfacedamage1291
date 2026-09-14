using ClosedXML.Excel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PickfaceDamage1291;

internal static class ExcelImportService
{
    public static ImportPreview Analyze(string filePath)
    {
        var hash = ComputeSha256(filePath);
        if (Database.HasImportedHash(hash))
            throw new InvalidOperationException("File này đã được nhập trước đó. Không cần cập nhật lại.");

        using var workbook = new XLWorkbook(filePath);
        var source = FindSourceSheet(workbook)
            ?? throw new InvalidDataException("Không tìm thấy đủ 3 cột bắt buộc: SKU, Tên sản phẩm, Base Units.");

        var ws = source.Value.Sheet;
        var headerRow = source.Value.HeaderRow;
        var skuCol = source.Value.SkuCol;
        var nameCol = source.Value.NameCol;
        var baseCol = source.Value.BaseCol;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;

        var bySku = new Dictionary<string, ProductCandidate>(StringComparer.OrdinalIgnoreCase);
        var sourceConflictSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceConflicts = new List<string>();
        var totalRows = 0;
        var invalidRows = 0;
        var duplicates = 0;

        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var sku = Clean(ws.Cell(row, skuCol).GetFormattedString());
            var name = Clean(ws.Cell(row, nameCol).GetFormattedString());
            var baseUnit = Clean(ws.Cell(row, baseCol).GetFormattedString()).ToUpperInvariant();

            if (sku.Length == 0 && name.Length == 0 && baseUnit.Length == 0) continue;
            totalRows++;

            if (sku.Length == 0 || name.Length == 0 || baseUnit.Length == 0)
            {
                invalidRows++;
                continue;
            }

            var candidate = new ProductCandidate(sku, name, baseUnit);
            if (!bySku.TryGetValue(sku, out var existing))
            {
                bySku[sku] = candidate;
                continue;
            }

            if (Same(existing, candidate))
            {
                duplicates++;
                continue;
            }

            if (sourceConflictSkus.Add(sku))
                sourceConflicts.Add($"SKU {sku}: có nhiều Tên sản phẩm/Base Units khác nhau ngay trong file nguồn.");
        }

        foreach (var sku in sourceConflictSkus) bySku.Remove(sku);
        invalidRows += sourceConflictSkus.Count;

        var newProducts = new List<ProductCandidate>();
        var unchanged = new List<ProductCandidate>();
        var conflicts = new List<ProductConflict>();

        foreach (var item in bySku.Values.OrderBy(x => x.Sku, StringComparer.OrdinalIgnoreCase))
        {
            var old = Database.GetProduct(item.Sku);
            if (old is null)
            {
                newProducts.Add(item);
            }
            else if (Clean(old.ProductName) == item.ProductName && Clean(old.BaseUnit).ToUpperInvariant() == item.BaseUnit)
            {
                unchanged.Add(item);
            }
            else
            {
                conflicts.Add(new ProductConflict(
                    item.Sku,
                    old.ProductName,
                    item.ProductName,
                    old.BaseUnit,
                    item.BaseUnit));
            }
        }

        return new ImportPreview
        {
            FilePath = filePath,
            FileHash = hash,
            TotalRows = totalRows,
            UniqueSkus = bySku.Count,
            InvalidRows = invalidRows,
            DuplicateRows = duplicates,
            NewProducts = newProducts,
            UnchangedProducts = unchanged,
            Conflicts = conflicts,
            SourceConflicts = sourceConflicts
        };
    }

    private static (IXLWorksheet Sheet, int HeaderRow, int SkuCol, int NameCol, int BaseCol)? FindSourceSheet(XLWorkbook workbook)
    {
        foreach (var ws in workbook.Worksheets)
        {
            var lastRow = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 0, 30);
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (var row = 1; row <= lastRow; row++)
            {
                int sku = 0, name = 0, baseUnit = 0;
                for (var col = 1; col <= lastCol; col++)
                {
                    var header = NormalizeHeader(ws.Cell(row, col).GetFormattedString());
                    if (header == "SKU") sku = col;
                    else if (header == "TEN SAN PHAM") name = col;
                    else if (header == "BASE UNITS") baseUnit = col;
                }
                if (sku > 0 && name > 0 && baseUnit > 0)
                    return (ws, row, sku, name, baseUnit);
            }
        }
        return null;
    }

    private static bool Same(ProductCandidate a, ProductCandidate b) =>
        string.Equals(a.ProductName, b.ProductName, StringComparison.Ordinal) &&
        string.Equals(a.BaseUnit, b.BaseUnit, StringComparison.OrdinalIgnoreCase);

    internal static string Clean(string value) =>
        string.Join(' ', (value ?? string.Empty)
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeHeader(string value)
    {
        var formD = Clean(value).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
