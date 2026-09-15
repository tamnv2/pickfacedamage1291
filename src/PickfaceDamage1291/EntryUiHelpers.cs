using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PickfaceDamage1291;

internal static partial class LocationNormalizer
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

internal static class ImageHashService
{
    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

internal sealed class ExclusiveShiftPicker : UserControl
{
    private readonly CheckBox _ca1 = new() { Text = "Ca 1", AutoSize = true };
    private readonly CheckBox _ca2 = new() { Text = "Ca 2", AutoSize = true };
    private bool _changing;

    public event EventHandler? SelectedShiftChanged;
    public string SelectedShift => _ca2.Checked ? "Ca 2" : _ca1.Checked ? "Ca 1" : string.Empty;

    public ExclusiveShiftPicker()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _ca1.Margin = new Padding(0, 5, 28, 0);
        _ca2.Margin = new Padding(0, 5, 0, 0);
        _ca1.CheckedChanged += (_, _) => Changed(_ca1, _ca2);
        _ca2.CheckedChanged += (_, _) => Changed(_ca2, _ca1);
        panel.Controls.AddRange([_ca1, _ca2]);
        Controls.Add(panel);
        SetShift("Ca 1");
    }

    public void SetShift(string? shift)
    {
        _changing = true;
        _ca1.Checked = !string.Equals(shift, "Ca 2", StringComparison.OrdinalIgnoreCase);
        _ca2.Checked = string.Equals(shift, "Ca 2", StringComparison.OrdinalIgnoreCase);
        _changing = false;
    }

    private void Changed(CheckBox source, CheckBox other)
    {
        if (_changing) return;
        _changing = true;
        if (source.Checked) other.Checked = false;
        else if (!other.Checked) source.Checked = true;
        _changing = false;
        SelectedShiftChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class TimeWheelPicker : UserControl
{
    private readonly Button _button = new();
    private int _hour;
    private int _minute;

    public int Hour => _hour;
    public int Minute => _minute;

    public TimeWheelPicker()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _button.AutoSize = false;
        _button.Width = 150;
        _button.Height = 36;
        _button.TextAlign = ContentAlignment.MiddleCenter;
        _button.Click += (_, _) => OpenWheel();
        Controls.Add(_button);
        SetValue(DateTime.Now.Hour, DateTime.Now.Minute);
    }

    public void SetValue(int hour, int minute)
    {
        _hour = Math.Clamp(hour, 0, 23);
        _minute = Math.Clamp(minute, 0, 59);
        _button.Text = $"{_hour:00} : {_minute:00}  ▾";
    }

    private void OpenWheel()
    {
        using var dialog = new TimeWheelDialog(_hour, _minute);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            SetValue(dialog.SelectedHour, dialog.SelectedMinute);
    }
}

internal sealed class TimeWheelDialog : Form
{
    private readonly ListBox _hours = new();
    private readonly ListBox _minutes = new();

    public int SelectedHour => Math.Max(0, _hours.SelectedIndex);
    public int SelectedMinute => Math.Max(0, _minutes.SelectedIndex);

    public TimeWheelDialog(int hour, int minute)
    {
        Text = "Chọn giờ phát hiện";
        Width = 390;
        Height = 500;
        MinimumSize = new Size(360, 440);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 11F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 2,
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        root.Controls.Add(new Label { Text = "GIỜ", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold) }, 0, 0);
        root.Controls.Add(new Label { Text = "PHÚT", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold) }, 1, 0);

        ConfigureWheel(_hours, Enumerable.Range(0, 24).Select(x => x.ToString("00")));
        ConfigureWheel(_minutes, Enumerable.Range(0, 60).Select(x => x.ToString("00")));
        _hours.SelectedIndex = Math.Clamp(hour, 0, 23);
        _minutes.SelectedIndex = Math.Clamp(minute, 0, 59);
        root.Controls.Add(_hours, 0, 1);
        root.Controls.Add(_minutes, 1, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        var ok = new Button { Text = "Chọn", AutoSize = true, Height = 36, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Hủy", AutoSize = true, Height = 36, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([ok, cancel]);
        root.Controls.Add(buttons, 0, 2);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
        _hours.DoubleClick += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        _minutes.DoubleClick += (_, _) => { DialogResult = DialogResult.OK; Close(); };
    }

    private static void ConfigureWheel(ListBox list, IEnumerable<string> values)
    {
        list.Dock = DockStyle.Fill;
        list.IntegralHeight = false;
        list.Font = new Font("Segoe UI", 16F);
        list.ItemHeight = 38;
        list.Items.AddRange(values.Cast<object>().ToArray());
        list.MouseWheel += (_, e) => MoveCircular(list, e.Delta > 0 ? -1 : 1, e);
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode is not (Keys.Up or Keys.Down)) return;
            MoveCircular(list, e.KeyCode == Keys.Up ? -1 : 1, null);
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
    }

    private static void MoveCircular(ListBox list, int delta, MouseEventArgs? mouse)
    {
        if (list.Items.Count == 0) return;
        var current = Math.Max(0, list.SelectedIndex);
        var next = (current + delta + list.Items.Count) % list.Items.Count;
        list.SelectedIndex = next;
        list.TopIndex = Math.Max(0, next - 4);
        if (mouse is HandledMouseEventArgs handled) handled.Handled = true;
    }
}
