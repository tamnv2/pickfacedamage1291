using System.Diagnostics;

namespace PickfaceDamage1291;

internal static class V140Runtime
{
    private const string LogsTabTag = "v140-logs-tab";

    public static void Apply(MainForm form)
    {
        AddLogsTab(form);
        form.Shown += (_, _) => AppLog.Info("MAIN_WINDOW_SHOWN", "Cửa sổ chính đã hiển thị.");
    }

    private static void AddLogsTab(MainForm form)
    {
        var tabs = FindAll<TabControl>(form).FirstOrDefault();
        if (tabs is null || tabs.TabPages.Cast<TabPage>().Any(x => Equals(x.Tag, LogsTabTag))) return;

        var tab = new TabPage("Logs") { Tag = LogsTabTag, BackColor = Color.WhiteSmoke };
        var control = new LogsControl();
        tab.Controls.Add(control);
        tabs.TabPages.Add(tab);
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var item in FindAll<T>(child))
            yield return item;
    }
}

internal sealed class LogsControl : UserControl
{
    private readonly Label _status = new();
    private readonly Button _send = new();

    public LogsControl()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(18);
        Font = new Font("Segoe UI", 10F);

        var group = new GroupBox
        {
            Text = "Nhật ký chẩn đoán ứng dụng",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };

        var description = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1050, 0),
            Text = "Ứng dụng ghi các sự kiện vận hành, đồng bộ và lỗi kỹ thuật để hỗ trợ chẩn đoán. Password, token, Authorization, cookie, credential và chuỗi xác thực được loại/redact trước khi ghi. Log được tự động gửi khi file hoàn tất/được xoay và vẫn giữ local trong 7 ngày."
        };

        _status.AutoSize = true;
        _status.Padding = new Padding(0, 10, 0, 8);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(0, 6, 0, 6)
        };
        var refresh = new Button { Text = "Làm mới", AutoSize = true };
        var openLocal = new Button { Text = "Mở thư mục logs local", AutoSize = true };
        var openDrive = new Button { Text = "Mở Logs trên Drive", AutoSize = true };
        _send.Text = "Gửi logs lên Drive";
        _send.AutoSize = true;

        refresh.Click += (_, _) => RefreshStatus();
        openLocal.Click += (_, _) =>
        {
            AppPaths.EnsureCreated();
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Logs) { UseShellExecute = true });
        };
        openDrive.Click += (_, _) => Process.Start(new ProcessStartInfo($"https://drive.google.com/drive/folders/{CloudConfig.DriveLogsFolderId}") { UseShellExecute = true });
        _send.Click += async (_, _) => await SendAsync();

        AppUiStyle.StyleButton(refresh, ButtonVisual.Normal);
        AppUiStyle.StyleButton(openLocal, ButtonVisual.Normal);
        AppUiStyle.StyleButton(openDrive, ButtonVisual.Normal);
        AppUiStyle.StyleButton(_send, ButtonVisual.Primary);
        actions.Controls.AddRange([refresh, openLocal, openDrive, _send]);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1050, 0),
            ForeColor = Color.DimGray,
            Text = "Log local không bị xoá sau khi gửi hoặc khi cập nhật phiên bản. Ứng dụng chỉ tự xoá log local quá 7 ngày; thư mục Logs trên Drive không bị tự động xoá."
        };

        root.Controls.Add(description, 0, 0);
        root.Controls.Add(_status, 0, 1);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(note, 0, 3);
        group.Controls.Add(root);
        Controls.Add(group);

        Load += (_, _) => RefreshStatus();
    }

    private void RefreshStatus()
    {
        var stats = AppLog.GetStats();
        _status.Text = $"Local: {stats.FileCount:N0} file • {FormatBytes(stats.Bytes)} | Drive: CẬP NHẬT HƯ HỎNG PICKFACE / Logs";
        _send.Enabled = stats.FileCount > 0 && GoogleService.IsConnected();
    }

    private async Task SendAsync()
    {
        if (!GoogleService.IsConnected())
        {
            MessageBox.Show(this, "Cần đăng nhập online và kết nối Google trước khi gửi logs.", "Chưa thể gửi logs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _send.Enabled = false;
        try
        {
            var progress = new Progress<string>(text => _status.Text = text);
            var sent = await AppLog.UploadAllAsync(progress);
            RefreshStatus();
            MessageBox.Show(this,
                sent == 0 ? "Không có file log mới/thay đổi cần gửi." : $"Đã gửi {sent:N0} file log lên Drive. Bản local vẫn được giữ trong 7 ngày.",
                "Logs",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Exception("LOG_UPLOAD_FAILED", ex);
            RefreshStatus();
            MessageBox.Show(this, ex.Message + "\n\nFile log chưa gửi thành công vẫn được giữ nguyên trên máy.", "Không gửi được logs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            RefreshStatus();
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:N1} KB";
        return $"{bytes / 1024d / 1024d:N1} MB";
    }
}
