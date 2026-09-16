from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]

def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8")

def write(rel, text):
    (ROOT / rel).write_text(text, encoding="utf-8")

def must_replace(rel, old, new, count=1):
    text = read(rel)
    if old not in text:
        raise SystemExit(f"PATCH MARKER NOT FOUND: {rel}: {old[:120]!r}")
    write(rel, text.replace(old, new, count))

must_replace(
    "src/PickfaceDamage1291/V147Runtime.cs",
    "            var gate = AttachAuthoritativeEntryGate(main);\n",
    "            // v1.4.9: do not attach the legacy entry gate. V148Runtime is the sole submit-state authority.\n",
)
must_replace(
    "src/PickfaceDamage1291/V147Runtime.cs",
    "                gate.Reevaluate();\n",
    "                // v1.4.9: submit validation is reevaluated by V148Runtime.\n",
)

v148_rel = "src/PickfaceDamage1291/V148Runtime.cs"
s = read(v148_rel)

for old, new in [
    ("            _sku.TextChanged += (_, _) => Evaluate();\n", "            _sku.TextChanged += (_, _) => ScheduleEvaluate();\n"),
    ("            _productName.TextChanged += (_, _) => Evaluate();\n", "            _productName.TextChanged += (_, _) => ScheduleEvaluate();\n"),
    ("            _location.TextChanged += (_, _) => Evaluate();\n", "            _location.TextChanged += (_, _) => ScheduleEvaluate();\n"),
    ("            _baseUnit.TextChanged += (_, _) => Evaluate();\n", "            _baseUnit.TextChanged += (_, _) => ScheduleEvaluate();\n"),
    ("            _status.TextChanged += (_, _) => Evaluate();\n", "            _status.TextChanged += (_, _) => ScheduleEvaluate();\n"),
]:
    if old not in s:
        raise SystemExit(f"V148 text-change marker not found: {old!r}")
    s = s.replace(old, new, 1)

old_fields = '''        private readonly DateTimePicker _date = new();
        private readonly Label _mode = new();
        private bool _showAll;
        private bool _rendering;
        private bool _scheduled;
'''
new_fields = '''        private readonly AvailableDatePickerV149 _date = new();
        private readonly CheckBox _showDate = new() { Text = "Hiển thị theo ngày", AutoSize = true, Checked = true };
        private readonly CheckBox _showAllMode = new() { Text = "Hiển thị tất cả", AutoSize = true };
        private readonly Label _mode = new();
        private bool _showAll;
        private bool _changingMode;
        private bool _rendering;
        private bool _scheduled;
'''
if old_fields not in s:
    raise SystemExit("V148 report filter field marker not found")
s = s.replace(old_fields, new_fields, 1)

start_marker = '''            filter.Controls.Add(new Label
            {
                Text = "Ngày nhập thực tế:",'''
end_marker = '''            _mode.AutoSize = true;'''
start = s.find(start_marker)
end = s.find(end_marker, start)
if start < 0 or end < 0:
    raise SystemExit("V148 report filter UI markers not found")

filter_block = '''            filter.Controls.Add(new Label
            {
                Text = "Chế độ hiển thị:",
                AutoSize = true,
                Padding = new Padding(0, 10, 8, 0)
            });

            _showDate.Margin = new Padding(2, 8, 14, 4);
            _showAllMode.Margin = new Padding(2, 8, 18, 4);
            _showDate.CheckedChanged += (_, _) =>
            {
                if (_changingMode) return;
                if (_showDate.Checked)
                {
                    ApplyFilterMode(false);
                }
                else if (!_showAllMode.Checked)
                {
                    _changingMode = true;
                    _showDate.Checked = true;
                    _changingMode = false;
                }
            };
            _showAllMode.CheckedChanged += (_, _) =>
            {
                if (_changingMode) return;
                if (_showAllMode.Checked)
                {
                    if (MessageBox.Show(
                            _main,
                            "Hiển thị toàn bộ dữ liệu có thể làm chậm ứng dụng khi số lượng phiếu lớn. Vẫn tiếp tục?",
                            "Hiển thị tất cả",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        _changingMode = true;
                        _showAllMode.Checked = false;
                        _showDate.Checked = true;
                        _changingMode = false;
                        return;
                    }
                    ApplyFilterMode(true);
                }
                else if (!_showDate.Checked)
                {
                    ApplyFilterMode(false);
                }
            };
            filter.Controls.Add(_showDate);
            filter.Controls.Add(_showAllMode);

            filter.Controls.Add(new Label
            {
                Text = "Ngày nhập thực tế:",
                AutoSize = true,
                Padding = new Padding(0, 10, 6, 0)
            });
            _date.Width = 150;
            _date.Margin = new Padding(2, 5, 8, 4);
            _date.ValueChanged += (_, _) =>
            {
                if (!_showAll && !_rendering) Render();
            };
            filter.Controls.Add(_date);

'''
s = s[:start] + filter_block + s[end:]

schedule_marker = '''        private void ScheduleRender()
        {'''
if schedule_marker not in s:
    raise SystemExit("V148 ScheduleRender marker not found")

apply_mode = '''        private void ApplyFilterMode(bool showAll)
        {
            _changingMode = true;
            try
            {
                _showAll = showAll;
                _showDate.Checked = !showAll;
                _showAllMode.Checked = showAll;
                _date.Enabled = !showAll && _date.HasAvailableDates;
            }
            finally
            {
                _changingMode = false;
            }
            Render();
        }

'''
s = s.replace(schedule_marker, apply_mode + schedule_marker, 1)

old_rows = '''                var rows = Database.GetReports(int.MaxValue);
                var selectedDate = _date.Value.Date;
'''
new_rows = '''                var rows = Database.GetReports(int.MaxValue);
                _date.SetAvailableDates(rows.Select(x => x.CreatedAt.ToLocalTime().Date));
                _date.Enabled = !_showAll && _date.HasAvailableDates;
                var selectedDate = _date.Value.Date;
'''
if old_rows not in s:
    raise SystemExit("V148 Render rows marker not found")
s = s.replace(old_rows, new_rows, 1)

old_export_call = '''                var result = await DamageReportExportServiceV148.ExportAsync(_main, selected, progress);
'''
new_export_call = '''                var result = await DamageReportExportServiceV148.ExportAsync(
                    _main,
                    selected,
                    select.SelectedDates,
                    select.SelectedShiftLabel,
                    progress);
'''
if old_export_call not in s:
    raise SystemExit("V148 export call marker not found")
s = s.replace(old_export_call, new_export_call, 1)

account_start = s.find('''                // Always start from the newest server profile.''')
account_end_marker = '''                SecureSessionStore.Save(session);
'''
account_end = s.find(account_end_marker, account_start)
if account_start < 0 or account_end < 0:
    raise SystemExit("V148 account update markers not found")
account_end += len(account_end_marker)
account_replacement = '''                var fresh = await FirebaseClient.UpdateOwnDisplayNameAsync(session, value);
                session.Profile = fresh;
'''
s = s[:account_start] + account_replacement + s[account_end:]
write(v148_rel, s)

available_date_code = r'''namespace PickfaceDamage1291;

internal sealed class AvailableDatePickerV149 : UserControl
{
    private readonly Button _button = new();
    private readonly List<DateTime> _available = [];
    private bool _updating;

    public event EventHandler? ValueChanged;

    public DateTime Value { get; private set; } = DateTime.Today;
    public bool HasAvailableDates => _available.Count > 0;

    public AvailableDatePickerV149()
    {
        AutoSize = false;
        Height = 36;
        Width = 150;
        _button.Dock = DockStyle.Fill;
        _button.Height = 36;
        _button.TextAlign = ContentAlignment.MiddleCenter;
        _button.Click += (_, _) => OpenCalendar();
        Controls.Add(_button);
        RefreshButton();
    }

    public void SetAvailableDates(IEnumerable<DateTime> dates)
    {
        var next = dates.Select(x => x.Date).Distinct().OrderBy(x => x).ToList();
        var same = _available.SequenceEqual(next);
        if (same)
        {
            RefreshButton();
            return;
        }

        _available.Clear();
        _available.AddRange(next);

        var old = Value.Date;
        if (_available.Count == 0)
        {
            Value = DateTime.Today;
        }
        else if (!_available.Contains(Value.Date))
        {
            Value = _available.Contains(DateTime.Today) ? DateTime.Today : _available[^1];
        }

        RefreshButton();
        if (old != Value.Date && !_updating)
            ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        RefreshButton();
    }

    private void RefreshButton()
    {
        _button.Text = _available.Count == 0 ? "Không có dữ liệu" : Value.ToString("dd/MM/yyyy");
        _button.Enabled = Enabled && _available.Count > 0;
    }

    private void OpenCalendar()
    {
        if (_available.Count == 0) return;
        using var dialog = new AvailableDateCalendarDialogV149(_available, Value);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || dialog.SelectedDate is null) return;

        var next = dialog.SelectedDate.Value.Date;
        if (next == Value.Date) return;

        _updating = true;
        try
        {
            Value = next;
            RefreshButton();
        }
        finally
        {
            _updating = false;
        }

        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class AvailableDateCalendarDialogV149 : Form
{
    private readonly HashSet<DateTime> _available;
    private readonly DateTime _minMonth;
    private readonly DateTime _maxMonth;
    private readonly Label _monthLabel = new();
    private readonly TableLayoutPanel _days = new();
    private DateTime _month;
    private readonly DateTime _initialSelected;

    public DateTime? SelectedDate { get; private set; }

    public AvailableDateCalendarDialogV149(IEnumerable<DateTime> available, DateTime selected)
    {
        _available = available.Select(x => x.Date).ToHashSet();
        _initialSelected = selected.Date;

        var min = _available.Min();
        var max = _available.Max();
        _minMonth = new DateTime(min.Year, min.Month, 1);
        _maxMonth = new DateTime(max.Year, max.Month, 1);
        _month = new DateTime(selected.Year, selected.Month, 1);
        if (_month < _minMonth) _month = _minMonth;
        if (_month > _maxMonth) _month = _maxMonth;

        Text = "Chọn ngày nhập thực tế";
        Width = 520;
        Height = 500;
        MinimumSize = new Size(500, 470);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));

        var previous = new Button { Text = "◀", Dock = DockStyle.Fill };
        var next = new Button { Text = "▶", Dock = DockStyle.Fill };

        _monthLabel.Dock = DockStyle.Fill;
        _monthLabel.TextAlign = ContentAlignment.MiddleCenter;
        _monthLabel.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);

        previous.Click += (_, _) =>
        {
            var candidate = _month.AddMonths(-1);
            if (candidate >= _minMonth)
            {
                _month = candidate;
                RenderCalendar(previous, next);
            }
        };

        next.Click += (_, _) =>
        {
            var candidate = _month.AddMonths(1);
            if (candidate <= _maxMonth)
            {
                _month = candidate;
                RenderCalendar(previous, next);
            }
        };

        nav.Controls.Add(previous, 0, 0);
        nav.Controls.Add(_monthLabel, 1, 0);
        nav.Controls.Add(next, 2, 0);
        root.Controls.Add(nav, 0, 0);

        _days.Dock = DockStyle.Fill;
        _days.ColumnCount = 7;
        _days.RowCount = 7;
        for (var i = 0; i < 7; i++)
            _days.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 7F));
        _days.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        for (var i = 1; i < 7; i++)
            _days.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 6F));

        var headers = new[] { "T2", "T3", "T4", "T5", "T6", "T7", "CN" };
        for (var i = 0; i < 7; i++)
        {
            _days.Controls.Add(new Label
            {
                Text = headers[i],
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
            }, i, 0);
        }
        root.Controls.Add(_days, 0, 1);

        var cancel = new Button { Text = "Hủy", AutoSize = true, DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 2);

        Controls.Add(root);
        CancelButton = cancel;
        RenderCalendar(previous, next);
    }

    private void RenderCalendar(Button previous, Button next)
    {
        _monthLabel.Text = _month.ToString("MM/yyyy");
        previous.Enabled = _month > _minMonth;
        next.Enabled = _month < _maxMonth;

        for (var i = _days.Controls.Count - 1; i >= 7; i--)
        {
            var control = _days.Controls[i];
            _days.Controls.RemoveAt(i);
            control.Dispose();
        }

        var first = new DateTime(_month.Year, _month.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7;
        var daysInMonth = DateTime.DaysInMonth(_month.Year, _month.Month);

        for (var slot = 0; slot < 42; slot++)
        {
            var row = 1 + slot / 7;
            var col = slot % 7;
            var dayNumber = slot - offset + 1;

            if (dayNumber < 1 || dayNumber > daysInMonth)
            {
                _days.Controls.Add(new Label { Dock = DockStyle.Fill }, col, row);
                continue;
            }

            var date = new DateTime(_month.Year, _month.Month, dayNumber);
            var available = _available.Contains(date);
            var button = new Button
            {
                Text = dayNumber.ToString(),
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                Enabled = available,
                ForeColor = available ? SystemColors.ControlText : SystemColors.GrayText,
                BackColor = available ? SystemColors.Window : SystemColors.ControlLight,
                UseVisualStyleBackColor = false
            };

            if (date == _initialSelected && available)
                button.Font = new Font(button.Font, FontStyle.Bold);

            if (available)
            {
                button.Click += (_, _) =>
                {
                    SelectedDate = date;
                    DialogResult = DialogResult.OK;
                    Close();
                };
            }

            _days.Controls.Add(button, col, row);
        }
    }
}
'''
write("src/PickfaceDamage1291/AvailableDatePickerV149.cs", available_date_code)

export_rel = "src/PickfaceDamage1291/DamageReportExportV148.cs"
e = read(export_rel)

filter_marker = '''    public List<DamageReport> Filter(IReadOnlyList<DamageReport> reports)
    {'''
if filter_marker not in e:
    raise SystemExit("Export dialog Filter marker not found")

selection_props = '''    public IReadOnlyList<DateTime> SelectedDates =>
        _dates.CheckedItems.Cast<DateChoice>().Select(x => x.Date.Date).OrderBy(x => x).ToList();

    public string SelectedShiftLabel =>
        _shift1.Checked && !_shift2.Checked ? "Ca 1" :
        !_shift1.Checked && _shift2.Checked ? "Ca 2" :
        string.Empty;

'''
e = e.replace(filter_marker, selection_props + filter_marker, 1)

old_sig = '''        IWin32Window owner,
        IReadOnlyList<DamageReport> selectedReports,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
'''
new_sig = '''        IWin32Window owner,
        IReadOnlyList<DamageReport> selectedReports,
        IReadOnlyList<DateTime> selectedEntryDates,
        string selectedShiftLabel,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
'''
if old_sig not in e:
    raise SystemExit("ExportAsync signature marker not found")
e = e.replace(old_sig, new_sig, 1)

name_start = e.find('''        var firstDetected = DetectionTime(reports[0]);''')
name_end = e.find('''        using var save = new SaveFileDialog''', name_start)
if name_start < 0 or name_end < 0:
    raise SystemExit("Export naming markers not found")

naming = '''        var scopeDates = selectedEntryDates
            .Select(x => x.Date)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        if (scopeDates.Count == 0)
            scopeDates = reports.Select(x => x.CreatedAt.ToLocalTime().Date).Distinct().OrderBy(x => x).ToList();

        var firstScopeDate = scopeDates[0];
        var lastScopeDate = scopeDates[^1];
        var oneScopeDate = scopeDates.Count == 1;
        var titleDateText = oneScopeDate
            ? $"NGÀY {firstScopeDate:dd/MM/yyyy}"
            : $"TỪ {firstScopeDate:dd/MM/yyyy} ĐẾN {lastScopeDate:dd/MM/yyyy}";
        var shiftText = string.IsNullOrWhiteSpace(selectedShiftLabel) ? string.Empty : selectedShiftLabel.Trim();
        var fileShiftSuffix = shiftText.Length == 0 ? string.Empty : $" - {shiftText}";
        var titleShiftSuffix = shiftText.Length == 0 ? string.Empty : $" - {shiftText}";

'''
e = e[:name_start] + naming + e[name_end:]

old_filename = '''            FileName = oneDetectionDate
                ? $"Thông tin hàng hư hỏng Pickface 1291 ngày {fileDateText}.xlsx"
                : $"Thông tin hàng hư hỏng Pickface 1291 từ {fileDateText}.xlsx",
'''
new_filename = '''            FileName = oneScopeDate
                ? $"Thông tin hàng hư hỏng Pickface 1291 ngày {firstScopeDate:ddMMyyyy}{fileShiftSuffix}.xlsx"
                : $"Thông tin hàng hư hỏng Pickface 1291 từ {firstScopeDate:ddMMyyyy} đến {lastScopeDate:ddMMyyyy}{fileShiftSuffix}.xlsx",
'''
if old_filename not in e:
    raise SystemExit("Export filename marker not found")
e = e.replace(old_filename, new_filename, 1)

old_title = '''        title.Value = $"DANH SÁCH HÀNG HỎNG PICKFACE 1291 - {titleDateText}";
'''
new_title = '''        title.Value = $"DANH SÁCH HÀNG HỎNG PICKFACE 1291 - {titleDateText}{titleShiftSuffix}";
'''
if old_title not in e:
    raise SystemExit("Export title marker not found")
e = e.replace(old_title, new_title, 1)
write(export_rel, e)

firebase_rel = "src/PickfaceDamage1291/FirebaseClient.cs"
f = read(firebase_rel)

server_marker = '''    public static async Task<long> GetServerNowMsAsync(FirebaseSession session, CancellationToken ct = default)
    {'''
if server_marker not in f:
    raise SystemExit("Firebase insertion marker not found")

method = '''    public static async Task<FirebaseUserProfile> UpdateOwnDisplayNameAsync(
        FirebaseSession session,
        string displayName,
        CancellationToken ct = default)
    {
        if (session.OfflineMode)
            throw new InvalidOperationException("Cần kết nối mạng để cập nhật họ tên.");

        var value = (displayName ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new InvalidOperationException("Họ tên không được để trống.");
        if (value.Length > 120)
            throw new InvalidOperationException("Họ tên tối đa 120 ký tự.");

        await EnsureFreshAsync(session, ct);
        using var response = await Http.PutAsync(
            DbUrl($"users/{Uri.EscapeDataString(session.Uid)}/display_name", session.IdToken),
            JsonContent(value),
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyDatabaseError(response.StatusCode, body));

        var refreshed = await GetProfileAsync(session.Uid, session.IdToken, ct)
                        ?? throw new InvalidOperationException("Không đọc lại được hồ sơ sau khi cập nhật họ tên.");
        session.Profile = refreshed;
        SecureSessionStore.Save(session);
        await AppendAuditAsync(session, "DISPLAY_NAME_UPDATED", new { display_name = refreshed.DisplayName }, ct: ct);
        return refreshed;
    }

'''
f = f.replace(server_marker, method + server_marker, 1)
write(firebase_rel, f)

rules_rel = "firebase/database.rules.json"
rules = json.loads(read(rules_rel))
uid_rules = rules["rules"]["users"]["$uid"]
uid_rules["display_name"] = {
    ".write": "auth != null && auth.uid == $uid && root.child('users').child(auth.uid).child('active').val() == true",
    ".validate": "newData.isString() && newData.val().length > 0 && newData.val().length <= 120",
}
write(rules_rel, json.dumps(rules, ensure_ascii=False, indent=2) + "\n")

csproj_rel = "src/PickfaceDamage1291/PickfaceDamage1291.csproj"
c = read(csproj_rel)
for old, new in [
    ("<Version>1.4.8</Version>", "<Version>1.4.9</Version>"),
    ("<InformationalVersion>1.4.8</InformationalVersion>", "<InformationalVersion>1.4.9</InformationalVersion>"),
    ("<AssemblyVersion>1.4.8.0</AssemblyVersion>", "<AssemblyVersion>1.4.9.0</AssemblyVersion>"),
    ("<FileVersion>1.4.8.0</FileVersion>", "<FileVersion>1.4.9.0</FileVersion>"),
]:
    if old not in c:
        raise SystemExit(f"Version marker not found: {old}")
    c = c.replace(old, new, 1)
write(csproj_rel, c)

print("v1.4.9 patch applied successfully")
