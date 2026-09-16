namespace PickfaceDamage1291;

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
