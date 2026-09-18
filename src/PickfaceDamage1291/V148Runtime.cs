using System.Reflection;

namespace PickfaceDamage1291;

/// <summary>
/// v1.4.8 correction layer. Runs last so the visible submit button, report list and
/// self-service account UI are corrected after the older runtime compatibility layers.
/// </summary>
internal static class V148Runtime
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Apply(MainForm main)
    {
        try
        {
            FixClippedActionRows(main);
            _ = new EntryGateV148(main);
            _ = new ReportListControllerV148(main);
            PatchOwnDisplayName(main);

            main.Shown += (_, _) =>
            {
                FixClippedActionRows(main);
                PatchOwnDisplayName(main);
            };

            AppLog.Info("V148_RUNTIME_APPLIED", "Đã áp dụng validation/UI/danh sách v1.4.8.");
        }
        catch (Exception ex)
        {
            AppLog.Exception("V148_RUNTIME_APPLY_FAILED", ex);
        }
    }

    private static void FixClippedActionRows(MainForm main)
    {
        var tabs = Field<TabControl>(main, "_tabs");

        var entryTab = tabs.TabPages.Cast<TabPage>()
            .FirstOrDefault(x => string.Equals(x.Text, "Nhập hư hỏng", StringComparison.Ordinal));
        if (entryTab is not null)
        {
            var imageBox = FindAll<GroupBox>(entryTab)
                .FirstOrDefault(x => x.Text.Contains("Hình ảnh hiện trạng", StringComparison.OrdinalIgnoreCase));
            var imageLayout = imageBox is null
                ? null
                : FindAll<TableLayoutPanel>(imageBox).FirstOrDefault(x => x.RowCount >= 3 && FindAll<FlowLayoutPanel>(x).Any());
            if (imageLayout is not null && imageLayout.RowStyles.Count > 0)
            {
                imageLayout.RowStyles[0].SizeType = SizeType.Absolute;
                imageLayout.RowStyles[0].Height = 72;
                var actions = FindAll<FlowLayoutPanel>(imageLayout).FirstOrDefault(x => x.Controls.OfType<Button>().Any());
                if (actions is not null)
                {
                    actions.AutoSize = false;
                    actions.Height = 68;
                    actions.Padding = new Padding(4, 7, 4, 7);
                    actions.Margin = Padding.Empty;
                    actions.WrapContents = true;
                }
            }
        }

        var reportsTab = tabs.TabPages.Cast<TabPage>()
            .FirstOrDefault(x => string.Equals(x.Text, "Danh sách đã nhập", StringComparison.Ordinal));
        if (reportsTab is not null)
        {
            var commands = FindAll<GroupBox>(reportsTab)
                .FirstOrDefault(x => string.Equals(x.Text, "Công cụ danh sách", StringComparison.Ordinal));
            var actionFlow = commands is null
                ? null
                : FindAll<FlowLayoutPanel>(commands)
                    .FirstOrDefault(x => x.Controls.OfType<Button>().Any(b => b.Text == "Làm mới"));
            var layout = actionFlow?.Parent as TableLayoutPanel;
            if (actionFlow is not null)
            {
                actionFlow.AutoSize = false;
                actionFlow.Height = 68;
                actionFlow.Padding = new Padding(2, 7, 2, 7);
                actionFlow.Margin = Padding.Empty;
                actionFlow.WrapContents = false;
            }
            if (layout is not null)
            {
                var row = layout.GetRow(actionFlow!);
                if (row >= 0 && row < layout.RowStyles.Count)
                {
                    layout.RowStyles[row].SizeType = SizeType.Absolute;
                    layout.RowStyles[row].Height = 72;
                }
            }
        }
    }

    private static void PatchOwnDisplayName(MainForm main)
    {
        var tabs = Field<TabControl>(main, "_tabs");
        var account = tabs.TabPages.Cast<TabPage>()
            .FirstOrDefault(x => string.Equals(x.Text, "Tài khoản", StringComparison.Ordinal));
        if (account is null) return;

        var control = FindAll<MyAccountControl>(account).FirstOrDefault();
        if (control is null || Equals(control.Tag, "v148-own-name")) return;

        var oldButton = FindAll<Button>(control)
            .FirstOrDefault(x => string.Equals(x.Text, "Lưu họ tên", StringComparison.Ordinal));
        if (oldButton?.Parent is not FlowLayoutPanel actions) return;

        var nameBox = GetPrivateField<TextBox>(control, "_name");
        var status = GetPrivateField<Label>(control, "_status");
        if (nameBox is null || status is null) return;

        var replacement = new Button
        {
            Text = "Lưu họ tên",
            AutoSize = true,
            MinimumSize = new Size(0, 42),
            Tag = "v148-own-name-save"
        };
        AppUiStyle.StyleButton(replacement, ButtonVisual.Primary);
        replacement.Click += async (_, _) =>
        {
            var session = AppSession.Current;
            if (session is null) return;
            if (session.OfflineMode)
            {
                NotificationCenter.Show(main, "Cần kết nối mạng để cập nhật họ tên.", "Tài khoản", MessageBoxIcon.Warning);
                return;
            }

            var value = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                NotificationCenter.Show(main, "Họ tên không được để trống.", "Tài khoản", MessageBoxIcon.Warning);
                return;
            }

            replacement.Enabled = false;
            try
            {
                var fresh = await FirebaseClient.UpdateOwnDisplayNameAsync(session, value);
                session.Profile = fresh;
                status.Text = "Đã cập nhật họ tên.";
                AppLog.Info("SELF_DISPLAY_NAME_UPDATED", "Người dùng đã cập nhật họ tên hiển thị.");
                NotificationCenter.Show(main, "Đã cập nhật họ tên.", "Tài khoản", MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppLog.Exception("SELF_DISPLAY_NAME_UPDATE_FAILED", ex);
                NotificationCenter.Show(main, ex.Message, "Không cập nhật được họ tên", MessageBoxIcon.Warning);
            }
            finally
            {
                replacement.Enabled = true;
            }
        };

        var index = actions.Controls.GetChildIndex(oldButton);
        oldButton.Visible = false;
        actions.Controls.Add(replacement);
        actions.Controls.SetChildIndex(replacement, Math.Max(0, index));
        control.Tag = "v148-own-name";
    }

    private static T Field<T>(object owner, string name) where T : class
        => owner.GetType().GetField(name, PrivateInstance)?.GetValue(owner) as T
           ?? throw new MissingFieldException(owner.GetType().Name, name);

    private static T? GetPrivateField<T>(object owner, string name) where T : class
        => owner.GetType().GetField(name, PrivateInstance)?.GetValue(owner) as T;

    internal static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }

    private sealed class EntryGateV148
    {
        private readonly MainForm _main;
        private readonly TextBox _sku;
        private readonly TextBox _productName;
        private readonly TextBox _location;
        private readonly ExclusiveShiftPicker _shift;
        private readonly NumericUpDown _quantity;
        private readonly TextBox _baseUnit;
        private readonly ListBox _images;
        private readonly List<string> _draftImages;
        private readonly Button _permissionSource;
        private readonly Button _visibleSubmit;
        private readonly Label _status;
        private readonly CheckBox? _overrideCheck;
        private readonly ComboBox? _overrideSelect;
        private readonly Label? _requiredNote;
        private bool _setting;
        private bool _scheduled;

        public EntryGateV148(MainForm main)
        {
            _main = main;
            _sku = Field<TextBox>(main, "_sku");
            _productName = Field<TextBox>(main, "_productName");
            _location = Field<TextBox>(main, "_location");
            _shift = Field<ExclusiveShiftPicker>(main, "_shift");
            _quantity = Field<NumericUpDown>(main, "_quantity");
            _baseUnit = Field<TextBox>(main, "_baseUnit");
            _images = Field<ListBox>(main, "_images");
            _draftImages = Field<List<string>>(main, "_draftImages");
            _permissionSource = Field<Button>(main, "_send");
            _status = Field<Label>(main, "_entryStatus");

            var tabs = Field<TabControl>(main, "_tabs");
            var entryTab = tabs.TabPages.Cast<TabPage>().First(x => x.Text == "Nhập hư hỏng");
            _visibleSubmit = FindAll<Button>(entryTab)
                .FirstOrDefault(x => x.Tag as string is "v132-submit" or "v132-saving")
                ?? FindAll<Button>(entryTab)
                    .First(x => x.Text.Contains("Gửi thông tin hư hỏng", StringComparison.OrdinalIgnoreCase));

            var productBox = FindAll<GroupBox>(entryTab).First(x => x.Text.StartsWith("1.", StringComparison.Ordinal));
            _overrideCheck = FindAll<CheckBox>(productBox)
                .FirstOrDefault(x => x.Text.Contains("Thay đổi Base Units", StringComparison.OrdinalIgnoreCase));
            _overrideSelect = FindAll<ComboBox>(productBox).FirstOrDefault();
            var submitBox = FindAll<GroupBox>(entryTab).FirstOrDefault(x => x.Text.StartsWith("4.", StringComparison.Ordinal));
            _requiredNote = submitBox is null
                ? null
                : FindAll<Label>(submitBox).FirstOrDefault(x => x.ForeColor == Color.Firebrick || x.Text.Contains("Chưa nhập đủ", StringComparison.OrdinalIgnoreCase));

            Attach(entryTab);
            Evaluate();
        }

        private void Attach(TabPage entryTab)
        {
            _sku.TextChanged += (_, _) => ScheduleEvaluate();
            _productName.TextChanged += (_, _) => ScheduleEvaluate();
            _location.TextChanged += (_, _) => ScheduleEvaluate();
            _shift.SelectedShiftChanged += (_, _) => Evaluate();
            _quantity.ValueChanged += (_, _) => Evaluate();
            _baseUnit.TextChanged += (_, _) => ScheduleEvaluate();
            _images.SelectedIndexChanged += (_, _) => Evaluate();
            if (_overrideCheck is not null) _overrideCheck.CheckedChanged += (_, _) => Evaluate();
            if (_overrideSelect is not null) _overrideSelect.SelectedIndexChanged += (_, _) => Evaluate();
            _status.TextChanged += (_, _) => ScheduleEvaluate();

            foreach (var button in FindAll<Button>(entryTab).Where(x =>
                         x.Text.Contains("Thêm ảnh", StringComparison.OrdinalIgnoreCase) ||
                         x.Text.Contains("Xoá ảnh", StringComparison.OrdinalIgnoreCase) ||
                         x.Text.Contains("Xóa ảnh", StringComparison.OrdinalIgnoreCase)))
                button.Click += (_, _) => ScheduleEvaluate();

            _permissionSource.EnabledChanged += (_, _) => ScheduleEvaluate();
            _visibleSubmit.EnabledChanged += (_, _) => ScheduleEvaluate();

            // MouseDown runs before Click. This closes the last race window created by the old
            // 350 ms mirror timer: an incomplete form can never reach the real submit Click handler.
            _visibleSubmit.MouseDown += (_, _) => Evaluate();
            _visibleSubmit.KeyDown += (_, e) =>
            {
                if (e.KeyCode is Keys.Enter or Keys.Space) Evaluate();
            };
        }

        private void ScheduleEvaluate()
        {
            if (_scheduled || _main.IsDisposed || !_main.IsHandleCreated) return;
            _scheduled = true;
            try
            {
                _main.BeginInvoke((Action)(() =>
                {
                    _scheduled = false;
                    if (!_main.IsDisposed) Evaluate();
                }));
            }
            catch
            {
                _scheduled = false;
            }
        }

        private void Evaluate()
        {
            if (_setting || _main.IsDisposed) return;
            var missing = MissingFields();
            var complete = missing.Count == 0;
            var allowed = complete && IsSessionAllowed();

            if (_requiredNote is not null)
            {
                _requiredNote.Visible = !complete;
                _requiredNote.Text = complete
                    ? string.Empty
                    : "Chưa nhập đủ thông tin bắt buộc nên không thể gửi. Thiếu: " + string.Join(", ", missing) + ".";
            }

            _setting = true;
            try
            {
                _permissionSource.Enabled = allowed;
                if (_visibleSubmit.Tag as string != "v132-saving")
                    _visibleSubmit.Enabled = allowed;
            }
            finally
            {
                _setting = false;
            }
        }

        private List<string> MissingFields()
        {
            var missing = new List<string>();
            var skuText = ExcelImportService.Clean(_sku.Text).ToUpperInvariant();
            var product = skuText.Length == 0 ? null : Database.GetProduct(skuText);
            if (product is null)
            {
                missing.Add("SKU hợp lệ");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_productName.Text) || _productName.ForeColor == Color.Firebrick)
                    missing.Add("Tên sản phẩm");

                if (_overrideCheck?.Checked == true)
                {
                    var selected = Convert.ToString(_overrideSelect?.SelectedItem)?.Trim();
                    if (string.IsNullOrWhiteSpace(selected)) missing.Add("Base Units thực tế hư hỏng");
                }
                else if (string.IsNullOrWhiteSpace(product.BaseUnit) || string.IsNullOrWhiteSpace(_baseUnit.Text))
                {
                    missing.Add("Base Units SKU");
                }
            }

            if (!LocationNormalizer.TryNormalize(_location.Text, out _, out _))
                missing.Add("Vị trí phát hiện hư hỏng thực tế");
            if (_quantity.Value < 1) missing.Add("Số lượng hư hỏng");
            if (string.IsNullOrWhiteSpace(_shift.SelectedShift)) missing.Add("Ca ghi nhận");
            if (_draftImages.Count < 1 || !_draftImages.Any(File.Exists)) missing.Add("ít nhất 1 ảnh hiện trạng");

            return missing.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private bool IsSessionAllowed()
        {
            var text = (_status.Text ?? string.Empty).Trim();
            if (text.Contains("Sẵn sàng gửi", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Contains("quyền nhập dự phòng", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Contains("Tạm khóa", StringComparison.OrdinalIgnoreCase)) return false;
            if (text.Contains("chưa đọc được trạng thái", StringComparison.OrdinalIgnoreCase)) return false;
            if (text.Contains("không có quyền", StringComparison.OrdinalIgnoreCase)) return false;

            if (AppSession.Current?.Profile.IsAdmin == true)
                return text.Contains("không có USER", StringComparison.OrdinalIgnoreCase);

            return AppSession.OperatorManager?.CanCreateDamage == true;
        }
    }

    private sealed class ReportListControllerV148
    {
        private readonly MainForm _main;
        private readonly TabControl _tabs;
        private readonly TabPage _tab;
        private readonly DataGridView _grid;
        private readonly Label _status;
        private readonly AvailableDatePickerV149 _date = new();
        private readonly CheckBox _showDate = new() { Text = "Hiển thị theo ngày", AutoSize = true, Checked = true };
        private readonly CheckBox _showAllMode = new() { Text = "Hiển thị tất cả", AutoSize = true };
        private readonly Label _mode = new();
        private bool _showAll;
        private bool _changingMode;
        private bool _rendering;
        private bool _scheduled;

        public ReportListControllerV148(MainForm main)
        {
            _main = main;
            _tabs = Field<TabControl>(main, "_tabs");
            _tab = _tabs.TabPages.Cast<TabPage>().First(x => x.Text == "Danh sách đã nhập");
            _grid = Field<DataGridView>(main, "_reportGrid");
            _status = Field<Label>(main, "_reportStatus");

            BuildFilterAndExportUi();
            _grid.ColumnAdded += (_, _) =>
            {
                if (!_rendering) ScheduleRender();
            };
            _tabs.SelectedIndexChanged += (_, _) =>
            {
                if (ReferenceEquals(_tabs.SelectedTab, _tab)) ScheduleRender();
            };
            _main.Shown += (_, _) => ScheduleRender();
            Render();
        }

        private void BuildFilterAndExportUi()
        {
            var commands = FindAll<GroupBox>(_tab).First(x => x.Text == "Công cụ danh sách");
            var actions = FindAll<FlowLayoutPanel>(commands)
                .First(x => x.Controls.OfType<Button>().Any(b => b.Text == "Làm mới"));
            var layout = actions.Parent as TableLayoutPanel;
            if (layout is null) return;

            actions.AutoSize = false;
            actions.Height = 68;
            actions.Padding = new Padding(2, 7, 2, 7);
            var actionRow = layout.GetRow(actions);
            if (actionRow >= 0 && actionRow < layout.RowStyles.Count)
            {
                layout.RowStyles[actionRow].SizeType = SizeType.Absolute;
                layout.RowStyles[actionRow].Height = 72;
            }

            var oldExport = actions.Controls.OfType<Button>()
                .FirstOrDefault(x => string.Equals(x.Text, "Xuất Excel", StringComparison.Ordinal));
            if (oldExport is not null)
            {
                var index = actions.Controls.GetChildIndex(oldExport);
                oldExport.Visible = false;
                var export = new Button { Text = "Xuất Excel", AutoSize = true, MinimumSize = new Size(0, 42), Tag = "v148-export" };
                AppUiStyle.StyleButton(export, ButtonVisual.Normal);
                export.Click += async (_, _) => await ExportAsync(export);
                actions.Controls.Add(export);
                actions.Controls.SetChildIndex(export, Math.Max(0, index));
            }

            var filter = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                WrapContents = true,
                Padding = new Padding(2, 6, 2, 5),
                Margin = Padding.Empty,
                Tag = "v148-report-filter"
            };
            filter.Controls.Add(new Label
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

            _mode.AutoSize = true;
            _mode.ForeColor = Color.DimGray;
            _mode.Padding = new Padding(8, 10, 0, 0);
            filter.Controls.Add(_mode);

            var filterRow = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(filter, 0, filterRow);
            ScheduleRender();
        }

        private void ApplyFilterMode(bool showAll)
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

        private void ScheduleRender()
        {
            if (_scheduled || _rendering || _main.IsDisposed || !_main.IsHandleCreated) return;
            _scheduled = true;
            try
            {
                _main.BeginInvoke((Action)(() =>
                {
                    _scheduled = false;
                    if (!_main.IsDisposed) Render();
                }));
            }
            catch
            {
                _scheduled = false;
            }
        }

        private void Render()
        {
            if (_rendering || _main.IsDisposed) return;
            _rendering = true;
            try
            {
                var rows = Database.GetReports(int.MaxValue);
                _date.SetAvailableDates(rows.Select(x => x.CreatedAt.ToLocalTime().Date));
                _date.Enabled = !_showAll && _date.HasAvailableDates;
                var selectedDate = _date.Value.Date;
                var visible = (_showAll
                        ? rows
                        : rows.Where(x => x.CreatedAt.ToLocalTime().Date == selectedDate))
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList();

                _grid.SuspendLayout();
                try
                {
                    _grid.Columns.Clear();
                    _grid.Rows.Clear();
                    _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ReportId", HeaderText = "ID", Visible = false });
                    AddColumn("Date", "Ngày phát hiện", 110);
                    AddColumn("Time", "Giờ phát hiện", 80);
                    AddColumn("Shift", "Ca ghi nhận", 90);
                    AddColumn("Sku", "SKU", 125);
                    AddColumn("Name", "Tên sản phẩm", 280);
                    AddColumn("Location", "Vị trí", 150);
                    AddColumn("Qty", "SL", 70);
                    AddColumn("Base", "Base Units", 90);
                    AddColumn("CreatedAt", "Thời gian nhập", 165);
                    AddColumn("CreatedBy", "Gửi thông tin bởi", 145);
                    AddColumn("Version", "Ver", 60);
                    AddColumn("Status", "Trạng thái", 125);
                    AddColumn("UpdatedAt", "Cập nhật cuối", 165);
                    AddColumn("UpdatedBy", "User cập nhật cuối", 145);
                    AddColumn("Error", "Lỗi", 300);

                    foreach (var report in visible)
                    {
                        var index = _grid.Rows.Add(
                            report.ReportId,
                            report.OccurredDate.ToString("dd/MM/yyyy"),
                            $"{report.Hour:00}:{report.Minute:00}",
                            report.Shift,
                            report.Sku,
                            report.ProductName,
                            report.Location,
                            decimal.Truncate(report.Quantity).ToString("0"),
                            report.BaseUnit,
                            report.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
                            string.IsNullOrWhiteSpace(report.CreatedBy) ? "-" : report.CreatedBy,
                            report.Version,
                            DisplayStatus(report.SyncStatus),
                            report.UpdatedAt.HasValue ? report.UpdatedAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "-",
                            string.IsNullOrWhiteSpace(report.UpdatedBy) ? "-" : report.UpdatedBy,
                            report.LastError ?? string.Empty);

                        if (!string.Equals(report.SyncStatus, "SYNCED", StringComparison.OrdinalIgnoreCase))
                            _grid.Rows[index].DefaultCellStyle.BackColor = Color.LightYellow;
                    }
                }
                finally
                {
                    _grid.ResumeLayout(true);
                }

                var pending = visible.Count(x => !string.Equals(x.SyncStatus, "SYNCED", StringComparison.OrdinalIgnoreCase));
                var allConflicts = rows.Count(x => string.Equals(x.SyncStatus, "CONFLICT", StringComparison.OrdinalIgnoreCase));
                var resolveConflict = GetPrivateField<Button>(_main, "_resolveConflictButton");
                if (resolveConflict is not null)
                {
                    resolveConflict.Visible = allConflicts > 0;
                    resolveConflict.Enabled = allConflicts > 0;
                }

                var baseStatus = _showAll
                    ? $"Toàn bộ {visible.Count:N0} | Chờ/lỗi đồng bộ: {pending:N0}"
                    : $"Ngày nhập {_date.Value:dd/MM/yyyy}: {visible.Count:N0} | Chờ/lỗi đồng bộ: {pending:N0}";
                _status.Text = allConflicts > 0
                    ? baseStatus + $" | Xung đột: {allConflicts:N0} — chọn phiếu và bấm Xử lý xung đột"
                    : baseStatus;
                _mode.Text = _showAll
                    ? "Đang hiển thị toàn bộ — dữ liệu lớn có thể làm chậm ứng dụng."
                    : "Danh sách lọc theo thời gian nhập thực tế, không theo thời gian phát hiện.";
            }
            catch (Exception ex)
            {
                AppLog.Exception("REPORT_LIST_V148_RENDER_FAILED", ex);
            }
            finally
            {
                _rendering = false;
            }
        }

        private void AddColumn(string name, string header, int width)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            });
        }

        private async Task ExportAsync(Button button)
        {
            try
            {
                button.Enabled = false;
                if (GoogleService.IsConnected())
                {
                    try
                    {
                        _status.Text = "Đang nhận dữ liệu mới nhất trước khi xuất Excel...";
                        await CloudSyncService.PullSharedDataAsync(new Progress<string>(s => _status.Text = s));
                    }
                    catch (Exception ex)
                    {
                        AppLog.Exception("EXPORT_PRE_SYNC_FAILED", ex);
                        if (MessageBox.Show(
                                _main,
                                "Không nhận được dữ liệu mới nhất từ Google. Nếu tiếp tục, file Excel chỉ phản ánh dữ liệu hiện có trên máy này.\n\n" + ex.Message + "\n\nTiếp tục?",
                                "Đồng bộ trước khi xuất chưa hoàn tất",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    }
                }
                else if (MessageBox.Show(
                             _main,
                             "Ứng dụng đang offline/chưa kết nối Google. File Excel chỉ phản ánh dữ liệu hiện có trên máy này. Tiếp tục?",
                             "Xuất dữ liệu local",
                             MessageBoxButtons.YesNo,
                             MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }

                var all = Database.GetReports(int.MaxValue);
                if (all.Count == 0)
                {
                    NotificationCenter.Show(_main, "Chưa có dữ liệu để xuất Excel.", "Xuất Excel", MessageBoxIcon.Information);
                    return;
                }

                using var select = new DamageExportSelectionDialogV148(all);
                if (select.ShowDialog(_main) != DialogResult.OK) return;
                var selected = select.Filter(all);
                if (selected.Count == 0)
                {
                    NotificationCenter.Show(_main, "Không có phiếu phù hợp ngày/ca đã chọn.", "Xuất Excel", MessageBoxIcon.Warning);
                    return;
                }

                var progress = new Progress<string>(s => _status.Text = s);
                var result = await DamageReportExportServiceV148.ExportAsync(
                    _main,
                    selected,
                    select.SelectedDates,
                    select.SelectedShiftLabel,
                    progress);
                if (result is null) return;
                _status.Text = $"Đã xuất {result.ReportCount:N0} phiếu.";
                var imageNote = result.MissingImages > 0 ? $" Không tải/nhúng được {result.MissingImages:N0} ảnh." : string.Empty;
                NotificationCenter.Show(_main, $"Đã xuất {result.ReportCount:N0} phiếu và {result.ImageCount:N0} ảnh.{imageNote}", "Xuất Excel", MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppLog.Exception("EXPORT_EXCEL_V148_FAILED", ex);
                NotificationCenter.Show(_main, ex.Message, "Không xuất được Excel", MessageBoxIcon.Error);
            }
            finally
            {
                button.Enabled = true;
                Render();
            }
        }

        private static string DisplayStatus(string status) => status switch
        {
            "SYNCED" => "Đã đồng bộ",
            "SYNCING" => "Đang đồng bộ",
            "ERROR" => "Lỗi đồng bộ",
            "OFFLINE_PENDING" => "Chờ online",
            "CONFLICT" => "Xung đột",
            _ => "Chờ đồng bộ"
        };
    }
}
