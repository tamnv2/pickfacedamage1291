from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one block in {path}, found {count}: {old[:180]!r}")
    write(path, text.replace(old, new, 1))


# Restore the business semantic from v1.3.2 while keeping v1.3.5 strict numeric/dot entry:
# 2 numeric segments => LTA, 3 numeric segments => Shelving.
old_location = r'''internal static partial class LocationNormalizer
{
    [GeneratedRegex(@"^(\d{1,2})\s*\.\s*(\d{1,2})(?:\s*\.\s*(\d{1,2}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex PositionRegex();

    public static bool TryNormalize(string? raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            error = "Chưa nhập vị trí phát hiện hư hỏng.";
            return false;
        }

        // Backward compatibility only for records created by earlier versions.
        if (text.StartsWith("LTA ", StringComparison.OrdinalIgnoreCase)) text = text[4..].Trim();
        else if (text.StartsWith("Shelving ", StringComparison.OrdinalIgnoreCase)) text = text[9..].Trim();

        var match = PositionRegex().Match(text);
        if (!match.Success)
        {
            error = "Vị trí chỉ được nhập số và dấu chấm, đúng dạng xx.yy hoặc xx.yy.zz. Ví dụ: 1.2 → 01.02; 1.2.3 → 01.02.03.";
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var a) ||
            !int.TryParse(match.Groups[2].Value, out var b) ||
            a is < 0 or > 99 || b is < 0 or > 99)
        {
            error = "Vị trí không hợp lệ.";
            return false;
        }

        if (!match.Groups[3].Success)
        {
            normalized = $"{a:00}.{b:00}";
            return true;
        }

        if (!int.TryParse(match.Groups[3].Value, out var c) || c is < 0 or > 99)
        {
            error = "Vị trí không hợp lệ.";
            return false;
        }

        normalized = $"{a:00}.{b:00}.{c:00}";
        return true;
    }
}
'''
new_location = r'''internal static partial class LocationNormalizer
{
    [GeneratedRegex(@"^(\d{1,2})\s*\.\s*(\d{1,2})(?:\s*\.\s*(\d{1,2}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex PositionRegex();

    [GeneratedRegex(@"^(?:LTA \d{2}\.\d{2}|Shelving \d{2}\.\d{2}\.\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalPositionRegex();

    public static bool IsCanonicalStoredValue(string? raw)
        => CanonicalPositionRegex().IsMatch((raw ?? string.Empty).Trim());

    public static string ToEditableInput(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.StartsWith("LTA ", StringComparison.OrdinalIgnoreCase)) return text[4..].Trim();
        if (text.StartsWith("Shelving ", StringComparison.OrdinalIgnoreCase)) return text[9..].Trim();
        return text;
    }

    public static bool TryNormalize(string? raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        var text = ToEditableInput(raw);
        if (text.Length == 0)
        {
            error = "Chưa nhập vị trí phát hiện hư hỏng.";
            return false;
        }

        var match = PositionRegex().Match(text);
        if (!match.Success)
        {
            error = "Vị trí chỉ được nhập số và dấu chấm, đúng dạng xx.yy (LTA) hoặc xx.yy.zz (Shelving). Ví dụ: 1.2 → LTA 01.02; 1.2.3 → Shelving 01.02.03.";
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var a) ||
            !int.TryParse(match.Groups[2].Value, out var b) ||
            a is < 0 or > 99 || b is < 0 or > 99)
        {
            error = "Vị trí không hợp lệ.";
            return false;
        }

        if (!match.Groups[3].Success)
        {
            normalized = $"LTA {a:00}.{b:00}";
            return true;
        }

        if (!int.TryParse(match.Groups[3].Value, out var c) || c is < 0 or > 99)
        {
            error = "Vị trí Shelving không hợp lệ.";
            return false;
        }

        normalized = $"Shelving {a:00}.{b:00}.{c:00}";
        return true;
    }
}
'''
replace_once("src/PickfaceDamage1291/EntryUiHelpers.cs", old_location, new_location)

# Preserve canonical LTA/Shelving values when the application assigns/loads them,
# but keep actual user editing limited to digits and dots. On focus, show only the editable numeric part.
old_attach = r'''    public static void AttachLocation(TextBox box)
    {
        if (BoundLocation.TryGetValue(box, out _)) return;
        BoundLocation.Add(box, new object());
        box.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.') e.Handled = true;
        };
        box.TextChanged += (_, _) => Sanitize(box, c => char.IsDigit(c) || c == '.');
    }

    private static void Sanitize(TextBox box, Func<char, bool> allowed)
'''
new_attach = r'''    public static void AttachLocation(TextBox box)
    {
        if (BoundLocation.TryGetValue(box, out _)) return;
        BoundLocation.Add(box, new object());
        box.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.') e.Handled = true;
        };
        box.Enter += (_, _) =>
        {
            var editable = LocationNormalizer.ToEditableInput(box.Text);
            if (string.Equals(editable, box.Text, StringComparison.Ordinal)) return;
            box.Text = editable;
            box.SelectionStart = box.TextLength;
        };
        box.TextChanged += (_, _) => SanitizeLocation(box);
    }

    private static void SanitizeLocation(TextBox box)
    {
        if (box.IsDisposed) return;
        var original = box.Text;

        // Canonical values are assigned by the application after validation or loaded from stored records.
        // A focused textbox is always an editing surface, so pasted text is still reduced to digits/dots.
        if (!box.Focused && LocationNormalizer.IsCanonicalStoredValue(original)) return;

        var cleaned = new string(original.Where(c => char.IsDigit(c) || c == '.').ToArray());
        if (string.Equals(original, cleaned, StringComparison.Ordinal)) return;
        var caret = Math.Min(box.SelectionStart, cleaned.Length);
        box.Text = cleaned;
        box.SelectionStart = caret;
    }

    private static void Sanitize(TextBox box, Func<char, bool> allowed)
'''
replace_once("src/PickfaceDamage1291/V133Runtime.cs", old_attach, new_attach)

# Release metadata.
replace_once(
    "src/PickfaceDamage1291/PickfaceDamage1291.csproj",
    '''    <Version>1.3.5</Version>\n    <InformationalVersion>1.3.5</InformationalVersion>\n    <AssemblyVersion>1.3.5.0</AssemblyVersion>\n    <FileVersion>1.3.5.0</FileVersion>\n''',
    '''    <Version>1.3.6</Version>\n    <InformationalVersion>1.3.6</InformationalVersion>\n    <AssemblyVersion>1.3.6.0</AssemblyVersion>\n    <FileVersion>1.3.6.0</FileVersion>\n'''
)

# Fail-fast semantic checks so the patch cannot silently ship the v1.3.3 regression again.
entry = read("src/PickfaceDamage1291/EntryUiHelpers.cs")
for required in (
    'normalized = $"LTA {a:00}.{b:00}";',
    'normalized = $"Shelving {a:00}.{b:00}.{c:00}";',
    'public static string ToEditableInput',
):
    if required not in entry:
        raise SystemExit(f"Missing required location semantic: {required}")

print("v1.3.6 LTA/Shelving location semantics restored")
