using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal sealed record HardDeleteOutcomeV1410(
    string ReportId,
    bool Deleted,
    bool AlreadyDeleted,
    int DeletedImages,
    string Error);

internal sealed record HardDeleteBatchV1410(
    IReadOnlyList<HardDeleteOutcomeV1410> Results,
    int DeletedReports,
    int DeletedImages);

internal static class HardDeleteGatewayV1410
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task<HardDeleteBatchV1410> DeleteAsync(
        IReadOnlyList<DamageReport> reports,
        string deletedBy,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var session = AppSession.Current ?? throw new InvalidOperationException("Chưa đăng nhập ứng dụng.");
        if (!session.Profile.IsAdmin) throw new InvalidOperationException("Chỉ ADMIN được xoá vĩnh viễn phiếu.");
        if (session.OfflineMode) throw new InvalidOperationException("Cần kết nối mạng để xoá vĩnh viễn dữ liệu Google.");

        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            await RuntimeConfigService.InitializeAsync(ct);
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            throw new InvalidOperationException("Google Gateway chưa sẵn sàng.");

        await FirebaseClient.EnsureFreshAsync(session, ct);
        var source = reports
            .Where(x => !string.IsNullOrWhiteSpace(x.ReportId))
            .GroupBy(x => x.ReportId, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToList();
        if (source.Count == 0) return new HardDeleteBatchV1410([], 0, 0);

        var outcomes = new List<HardDeleteOutcomeV1410>(source.Count);
        var deletedReports = 0;
        var deletedImages = 0;
        var processed = 0;

        foreach (var batch in source.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Đang xoá Google {processed + 1:N0}-{processed + batch.Length:N0}/{source.Count:N0}...");
            var payload = new
            {
                reports = batch.Select(report => new
                {
                    report_id = report.ReportId,
                    sku = report.Sku,
                    version = Math.Max(1, report.Version),
                    created_at = report.CreatedAt.ToString("O"),
                    created_by = report.CreatedBy
                }).ToArray(),
                deleted_by = deletedBy,
                device_id = SecureSessionStore.GetOrCreateDeviceId(),
                session_id = AppSession.OperatorManager?.SessionId ?? string.Empty,
                client_time = DateTimeOffset.Now.ToString("O")
            };

            var root = await CallAsync("hard_delete_reports", payload, ct);
            if (root.TryGetProperty("results", out var resultsNode) && resultsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resultsNode.EnumerateArray())
                {
                    var id = item.TryGetProperty("report_id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
                    var deleted = item.TryGetProperty("deleted", out var deletedNode) && deletedNode.ValueKind == JsonValueKind.True;
                    var already = item.TryGetProperty("already_deleted", out var alreadyNode) && alreadyNode.ValueKind == JsonValueKind.True;
                    var images = item.TryGetProperty("deleted_images", out var imageNode) && imageNode.TryGetInt32(out var imageCount) ? imageCount : 0;
                    var error = item.TryGetProperty("error", out var errorNode) ? errorNode.GetString() ?? string.Empty : string.Empty;
                    outcomes.Add(new HardDeleteOutcomeV1410(id, deleted, already, images, error));
                }
            }

            deletedReports += root.TryGetProperty("deleted_reports", out var reportCountNode) && reportCountNode.TryGetInt32(out var reportCount) ? reportCount : 0;
            deletedImages += root.TryGetProperty("deleted_images", out var imageCountNode) && imageCountNode.TryGetInt32(out var totalImageCount) ? totalImageCount : 0;
            processed += batch.Length;
        }

        progress?.Report($"Google đã xử lý {deletedReports:N0}/{source.Count:N0} phiếu, {deletedImages:N0} ảnh.");
        return new HardDeleteBatchV1410(outcomes, deletedReports, deletedImages);
    }

    private static async Task<JsonElement> CallAsync(string action, object payload, CancellationToken ct)
    {
        var session = AppSession.Current ?? throw new InvalidOperationException("Chưa đăng nhập ứng dụng.");
        await FirebaseClient.EnsureFreshAsync(session, ct);
        var started = Stopwatch.StartNew();
        AppLog.Info("HARD_DELETE_GATEWAY_START", "Bắt đầu yêu cầu xoá vĩnh viễn Google.", new Dictionary<string, object?> { ["action"] = action });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, RuntimeConfigService.GoogleGatewayUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    action,
                    id_token = session.IdToken,
                    payload
                }), Encoding.UTF8, "application/json")
            };
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Google Gateway lỗi HTTP {(int)response.StatusCode}.");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            {
                var error = root.TryGetProperty("error", out var errorNode) ? errorNode.GetString() : null;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Google từ chối yêu cầu xoá." : error);
            }

            AppLog.Info("HARD_DELETE_GATEWAY_OK", "Google xác nhận yêu cầu xoá vĩnh viễn.", new Dictionary<string, object?>
            {
                ["elapsed_ms"] = started.ElapsedMilliseconds,
                ["action"] = action
            });
            return root.Clone();
        }
        catch (Exception ex)
        {
            AppLog.Exception("HARD_DELETE_GATEWAY_FAILED", ex, new Dictionary<string, object?>
            {
                ["elapsed_ms"] = started.ElapsedMilliseconds,
                ["action"] = action
            });
            throw;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = NetworkHttpClientFactory.Create(TimeSpan.FromMinutes(4), "PickfaceDamage1291-HardDelete");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}

internal static class V1410Runtime
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Apply(MainForm main)
    {
        try
        {
            FixReportFilterArea(main);
            DisableSelfDisplayName(main);
            ReplaceDeleteButton(main);
            main.Shown += (_, _) =>
            {
                FixReportFilterArea(main);
                DisableSelfDisplayName(main);
                ReplaceDeleteButton(main);
            };
            AppLog.Info("V1410_RUNTIME_APPLIED", "Đã áp dụng UI/xoá vĩnh viễn v1.4.10.");
        }
        catch (Exception ex)
        {
            AppLog.Exception("V1410_RUNTIME_APPLY_FAILED", ex);
        }
    }

    private static void FixReportFilterArea(MainForm main)
    {
        var tabs = Field<TabControl>(main, "_tabs");
        var tab = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Danh sách đã nhập");
        if (tab is null) return;

        var filter = FindAll<FlowLayoutPanel>(tab).FirstOrDefault(x => string.Equals(x.Tag as string, "v148-report-filter", StringComparison.Ordinal));
        if (filter is null) return;
        filter.AutoSize = false;
        filter.Height = 78;
        filter.MinimumSize = new Size(0, 78);
        filter.Padding = new Padding(4, 9, 4, 12);
        filter.WrapContents = true;

        var picker = FindAll<AvailableDatePickerV149>(filter).FirstOrDefault();
        if (picker is not null)
        {
            picker.Width = 190;
            picker.Height = 44;
            picker.MinimumSize = new Size(190, 44);
            picker.Margin = new Padding(3, 4, 14, 10);
        }

        if (filter.Parent is TableLayoutPanel layout)
        {
            var row = layout.GetRow(filter);
            if (row >= 0 && row < layout.RowStyles.Count)
            {
                layout.RowStyles[row].SizeType = SizeType.Absolute;
                layout.RowStyles[row].Height = 84;
            }
        }

        Control? parent = filter.Parent;
        while (parent is not null && parent is not GroupBox) parent = parent.Parent;
        if (parent is GroupBox group)
            group.MinimumSize = new Size(0, Math.Max(270, group.MinimumSize.Height));
    }

    private static void DisableSelfDisplayName(MainForm main)
    {
        var tabs = Field<TabControl>(main, "_tabs");
        var tab = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Tài khoản");
        if (tab is null) return;
        var control = FindAll<MyAccountControl>(tab).FirstOrDefault();
        if (control is null) return;

        var name = GetPrivateField<TextBox>(control, "_name");
        if (name is not null)
        {
            name.ReadOnly = true;
            name.TabStop = false;
            name.BackColor = SystemColors.Control;
        }
        foreach (var button in FindAll<Button>(control).Where(x => x.Text == "Lưu họ tên"))
        {
            button.Visible = false;
            button.Enabled = false;
        }
        var status = GetPrivateField<Label>(control, "_status");
        if (status is not null) status.Text = "Họ tên hiển thị do ADMIN quản lý.";
    }

    private static void ReplaceDeleteButton(MainForm main)
    {
        var tabs = Field<TabControl>(main, "_tabs");
        var tab = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Danh sách đã nhập");
        if (tab is null || FindAll<Button>(tab).Any(x => string.Equals(x.Tag as string, "v1410-hard-delete", StringComparison.Ordinal))) return;

        var old = FindAll<Button>(tab).FirstOrDefault(x => x.Text.Contains("Xoá phiếu đã chọn", StringComparison.OrdinalIgnoreCase));
        var actions = old?.Parent as FlowLayoutPanel ?? FindAll<FlowLayoutPanel>(tab)
            .FirstOrDefault(x => x.Controls.OfType<Button>().Any(b => b.Text == "Làm mới"));
        if (actions is null) return;

        var index = old is null ? actions.Controls.Count : actions.Controls.GetChildIndex(old);
        if (old is not null)
        {
            old.Visible = false;
            old.Enabled = false;
        }

        var replacement = new Button
        {
            Text = "Xoá phiếu đã chọn (ADMIN)",
            AutoSize = true,
            MinimumSize = new Size(0, 42),
            Tag = "v1410-hard-delete",
            Visible = AppSession.Current?.Profile.IsAdmin == true
        };
        AppUiStyle.StyleButton(replacement, ButtonVisual.Danger);
        var controller = new DeleteController(main, replacement);
        replacement.Click += controller.OnClick;
        actions.Controls.Add(replacement);
        actions.Controls.SetChildIndex(replacement, Math.Clamp(index, 0, actions.Controls.Count - 1));
    }

    private sealed class DeleteController
    {
        private readonly MainForm _main;
        private readonly Button _button;
        private readonly DataGridView _grid;
        private readonly Label _status;
        private bool _busy;

        public DeleteController(MainForm main, Button button)
        {
            _main = main;
            _button = button;
            _grid = Field<DataGridView>(main, "_reportGrid");
            _status = Field<Label>(main, "_reportStatus");
        }

        public async void OnClick(object? sender, EventArgs e)
        {
            if (_busy) return;
            try { await ExecuteAsync(); }
            catch (Exception ex)
            {
                AppLog.Exception("DAMAGE_REPORTS_HARD_DELETE_FAILED", ex);
                MessageBox.Show(_main, ex.Message, "Không xoá được phiếu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task ExecuteAsync()
        {
            var session = AppSession.Current;
            if (session is null || !session.Profile.IsAdmin)
            {
                MessageBox.Show(_main, "Chỉ ADMIN được xoá phiếu đã nhập.", "Không có quyền", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var ids = _grid.SelectedRows.Cast<DataGridViewRow>()
                .Select(row => Convert.ToString(row.Cells["ReportId"].Value) ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ids.Count == 0)
            {
                MessageBox.Show(_main, "Chọn ít nhất một phiếu cần xoá.", "Chưa chọn phiếu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!GoogleService.IsConnected())
            {
                MessageBox.Show(_main, "Cần kết nối Google để xoá thực sự dòng Google Sheet và ảnh Drive.", "Chưa thể xoá an toàn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var reports = ids.Select(Database.GetReportById).Where(x => x is not null).Cast<DamageReport>().ToList();
            if (reports.Count == 0)
            {
                RefreshReports();
                return;
            }

            if (MessageBox.Show(
                    _main,
                    $"Xoá vĩnh viễn {reports.Count:N0} phiếu đã chọn?\n\nDòng tương ứng trên Google Sheet và các ảnh Drive của phiếu sẽ bị xoá thực sự. Chỉ lịch sử thao tác xoá được giữ lại.\n\nHành động này không thể hoàn tác.",
                    "Xác nhận xoá vĩnh viễn",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;

            using var password = new AdminPasswordConfirmDialog(reports.Count);
            if (password.ShowDialog(_main) != DialogResult.OK) return;

            _busy = true;
            _button.Enabled = false;
            _main.UseWaitCursor = false;
            _main.Cursor = Cursors.Default;
            try
            {
                _status.Text = "Đang xác minh mật khẩu ADMIN...";
                await Task.Yield();
                await AccountSelfService.VerifyCurrentPasswordAsync(session, password.Password);

                _status.Text = $"Đang chuẩn bị xoá {reports.Count:N0} phiếu...";
                await Task.Run(AppPaths.BackupDatabase);

                var result = await HardDeleteGatewayV1410.DeleteAsync(
                    reports,
                    session.Profile.Username,
                    new Progress<string>(text => _status.Text = text));

                var byId = reports.ToDictionary(x => x.ReportId, StringComparer.Ordinal);
                var deletedLocal = 0;
                var failures = new List<string>();
                foreach (var outcome in result.Results)
                {
                    if ((outcome.Deleted || outcome.AlreadyDeleted) && byId.TryGetValue(outcome.ReportId, out var report))
                    {
                        if (Database.GetReportById(report.ReportId) is null || Database.DeleteDamageReport(report.ReportId, session.Profile.Username, true))
                            deletedLocal++;
                        else
                            failures.Add($"{report.Sku}: Google đã xoá nhưng local chưa xoá được.");
                    }
                    else if (!string.IsNullOrWhiteSpace(outcome.Error))
                    {
                        var key = byId.TryGetValue(outcome.ReportId, out var failedReport) ? failedReport.Sku : outcome.ReportId;
                        failures.Add($"{key}: {outcome.Error}");
                    }
                }

                var returned = result.Results.Select(x => x.ReportId).ToHashSet(StringComparer.Ordinal);
                foreach (var report in reports.Where(x => !returned.Contains(x.ReportId)))
                    failures.Add($"{report.Sku}: Google không trả kết quả xoá.");

                AppLog.Info("DAMAGE_REPORTS_HARD_DELETE_DONE", "Hoàn tất xoá vĩnh viễn phiếu.", new Dictionary<string, object?>
                {
                    ["requested"] = reports.Count,
                    ["deleted_local"] = deletedLocal,
                    ["deleted_remote"] = result.DeletedReports,
                    ["deleted_drive_images"] = result.DeletedImages,
                    ["failures"] = failures.Count
                });
                RefreshReports();

                if (failures.Count == 0 && deletedLocal == reports.Count)
                {
                    MessageBox.Show(_main, $"Đã xoá vĩnh viễn {deletedLocal:N0} phiếu và {result.DeletedImages:N0} ảnh liên quan. Lịch sử thao tác vẫn được giữ.", "Đã xoá vĩnh viễn", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var detail = string.Join("\n", failures.Take(8));
                if (failures.Count > 8) detail += $"\n... và {failures.Count - 8:N0} lỗi khác.";
                MessageBox.Show(_main, $"Đã hoàn tất {deletedLocal:N0}/{reports.Count:N0} phiếu. Phiếu chưa hoàn tất vẫn được giữ local để thử lại.\n\n{detail}", "Xoá chưa hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _busy = false;
                _button.Enabled = true;
                _main.UseWaitCursor = false;
                _main.Cursor = Cursors.Default;
                RefreshReports();
            }
        }

        private void RefreshReports()
        {
            _main.GetType().GetMethod("RefreshReports", PrivateInstance)?.Invoke(_main, null);
        }
    }

    private static T Field<T>(object owner, string name) where T : class
        => owner.GetType().GetField(name, PrivateInstance)?.GetValue(owner) as T
           ?? throw new MissingFieldException(owner.GetType().Name, name);

    private static T? GetPrivateField<T>(object owner, string name) where T : class
        => owner.GetType().GetField(name, PrivateInstance)?.GetValue(owner) as T;

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }
}
