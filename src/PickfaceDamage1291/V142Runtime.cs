using System.Text.RegularExpressions;

namespace PickfaceDamage1291;

internal static class V142Runtime
{
    public static void Apply(Form form)
    {
        form.Load += (_, _) =>
        {
            EntryCompletenessGuard.Attach(form);
            TaskProgressCenter.Attach(form);
            NetworkRecoveryCoordinator.Attach(form);
        };
    }

    private static class EntryCompletenessGuard
    {
        public static void Attach(Form form)
        {
            var tabs = FindAll<TabControl>(form).FirstOrDefault();
            var tab = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Text == "Nhập hư hỏng");
            if (tab is null) return;

            var productBox = FindAll<GroupBox>(tab).FirstOrDefault(x => x.Text.StartsWith("1.", StringComparison.Ordinal));
            var occurrenceBox = FindAll<GroupBox>(tab).FirstOrDefault(x => x.Text.StartsWith("2.", StringComparison.Ordinal));
            var submitBox = FindAll<GroupBox>(tab).FirstOrDefault(x => x.Text.StartsWith("4.", StringComparison.Ordinal));
            if (productBox is null || occurrenceBox is null || submitBox is null) return;

            var productForm = FindAll<TableLayoutPanel>(productBox).FirstOrDefault(x => x.ColumnCount == 2 && x.RowCount >= 3);
            var occurrenceForm = FindAll<TableLayoutPanel>(occurrenceBox).FirstOrDefault(x => x.ColumnCount == 2 && x.RowCount >= 5);
            var send = FindAll<Button>(submitBox).FirstOrDefault(x => x.Text.Contains("Gửi thông tin hư hỏng", StringComparison.OrdinalIgnoreCase));
            if (productForm is null || occurrenceForm is null || send is null) return;

            var sku = productForm.GetControlFromPosition(1, 0) as TextBox;
            var productName = productForm.GetControlFromPosition(1, 1) as TextBox;
            var baseUnit = productForm.GetControlFromPosition(1, 2) as TextBox;
            var location = occurrenceForm.GetControlFromPosition(1, 0) as TextBox;
            var shift = occurrenceForm.GetControlFromPosition(1, 3) as ExclusiveShiftPicker;
            var quantity = occurrenceForm.GetControlFromPosition(1, 4) as NumericUpDown;
            var status = FindAll<Label>(submitBox).FirstOrDefault();
            if (sku is null || productName is null || baseUnit is null || location is null || shift is null || quantity is null) return;

            var toolTip = new ToolTip { AutoPopDelay = 7000, InitialDelay = 300, ReshowDelay = 150 };
            var applying = false;
            var externalAllowed = send.Enabled;

            // ADMIN's active_operator check is asynchronous. Fail closed until the status line
            // explicitly confirms the send path is available.
            if (AppSession.Current?.Profile.IsAdmin == true && !StatusAllowsSend(status?.Text))
                externalAllowed = false;

            bool FieldsReady()
            {
                var skuText = sku.Text.Trim();
                var validSku = skuText.Length > 0 && skuText.All(c => c is >= '0' and <= '9');
                var validProduct = validSku &&
                                   !string.IsNullOrWhiteSpace(productName.Text) &&
                                   !string.IsNullOrWhiteSpace(baseUnit.Text) &&
                                   productName.ForeColor != Color.Firebrick;
                var validLocation = LocationNormalizer.TryNormalize(location.Text, out _, out _);
                return validProduct && validLocation && !string.IsNullOrWhiteSpace(shift.SelectedShift) && quantity.Value >= 1;
            }

            void ApplyState()
            {
                if (form.IsDisposed || send.IsDisposed) return;
                var ready = FieldsReady();
                var enabled = externalAllowed && ready;
                applying = true;
                try { send.Enabled = enabled; }
                finally { applying = false; }

                toolTip.SetToolTip(send,
                    !ready
                        ? "Nhập đủ SKU hợp lệ, vị trí, ngày/giờ, ca và số lượng để mở khóa nút Gửi."
                        : !externalAllowed
                            ? "Thông tin đã đủ nhưng quyền gửi đang tạm khóa theo trạng thái phiên hiện tại."
                            : string.Empty);
            }

            void ExternalEnabledChanged(object? _, EventArgs __)
            {
                if (applying) return;
                externalAllowed = send.Enabled;
                ApplyState();
            }

            void StatusChanged(object? _, EventArgs __)
            {
                var parsed = ParseStatusPermission(status?.Text);
                if (parsed.HasValue) externalAllowed = parsed.Value;
                ApplyState();
            }

            sku.TextChanged += (_, _) => ApplyState();
            productName.TextChanged += (_, _) => ApplyState();
            baseUnit.TextChanged += (_, _) => ApplyState();
            location.TextChanged += (_, _) => ApplyState();
            shift.SelectedShiftChanged += (_, _) => ApplyState();
            quantity.ValueChanged += (_, _) => ApplyState();
            send.EnabledChanged += ExternalEnabledChanged;
            if (status is not null) status.TextChanged += StatusChanged;
            form.FormClosed += (_, _) => toolTip.Dispose();
            ApplyState();
        }

        private static bool StatusAllowsSend(string? text) => ParseStatusPermission(text) == true;

        private static bool? ParseStatusPermission(string? text)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.Contains("Sẵn sàng gửi", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Contains("quyền nhập dự phòng", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Contains("Tạm khóa", StringComparison.OrdinalIgnoreCase)) return false;
            if (value.Contains("chưa đọc được trạng thái", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }
    }

    private static class TaskProgressCenter
    {
        private sealed record TaskState(string Key, string Message, int? Percent, DateTime UpdatedUtc);

        private sealed class Host
        {
            public required Form Owner { get; init; }
            public required Panel Surface { get; init; }
            public required FlowLayoutPanel Stack { get; init; }
            public Dictionary<string, TaskState> Tasks { get; } = new(StringComparer.Ordinal);
        }

        private static readonly ConditionalWeakTable<Form, Host> Hosts = new();
        private static readonly Regex FractionRegex = new(@"(?<done>[\d\.,]+)\s*/\s*(?<total>[\d\.,]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex PercentRegex = new(@"(?<percent>\d{1,3})\s*%", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static void Attach(Form form)
        {
            if (Hosts.TryGetValue(form, out _)) return;

            var surface = new Panel
            {
                Width = 330,
                Height = 68,
                BackColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Padding = new Padding(7),
                Tag = "v142-task-progress"
            };
            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                BackColor = Color.WhiteSmoke,
                Padding = Padding.Empty
            };
            surface.Controls.Add(stack);
            form.Controls.Add(surface);
            surface.BringToFront();

            var host = new Host { Owner = form, Surface = surface, Stack = stack };
            Hosts.Add(form, host);
            Reposition(host);

            form.Resize += (_, _) => Reposition(host);
            form.BeginInvoke((Action)(() => ObserveStatusLabels(host)));

            Action<BackgroundSyncState> syncHandler = state =>
            {
                if (form.IsDisposed) return;

                // Only show work that is actually executing. Merely waiting in a queue or an
                // idle/status message must not occupy the progress corner.
                if (state.IsBusy && !state.Completed)
                    Report(form, "background-report-sync", state.Message, state.Percent is > 0 and <= 100 ? state.Percent : null);
                else
                    Complete(form, "background-report-sync");
            };
            BackgroundSyncCoordinator.StateChanged += syncHandler;
            form.FormClosed += (_, _) => BackgroundSyncCoordinator.StateChanged -= syncHandler;
        }

        public static void Report(Form form, string key, string message, int? percent = null)
        {
            if (form.IsDisposed) return;
            void Work()
            {
                if (!Hosts.TryGetValue(form, out var host) || host.Owner.IsDisposed) return;
                host.Tasks[key] = new TaskState(key, CleanMessage(message), percent is null ? null : Math.Clamp(percent.Value, 0, 100), DateTime.UtcNow);
                Rebuild(host);
            }
            if (form.InvokeRequired) form.BeginInvoke((Action)Work); else Work();
        }

        public static void Complete(Form form, string key)
        {
            if (form.IsDisposed) return;
            void Work()
            {
                if (!Hosts.TryGetValue(form, out var host)) return;
                if (host.Tasks.Remove(key)) Rebuild(host);
            }
            if (form.InvokeRequired) form.BeginInvoke((Action)Work); else Work();
        }

        private static void ObserveStatusLabels(Host host)
        {
            if (host.Owner.IsDisposed) return;
            var labels = FindAll<Label>(host.Owner).Where(x => x != null).Distinct().ToList();
            foreach (var label in labels)
            {
                if (ReferenceEquals(label.Parent, host.Stack) || IsInside(label, host.Surface)) continue;
                var key = "label-" + label.GetHashCode().ToString("X");
                void Changed(object? _, EventArgs __)
                {
                    var text = label.Text?.Trim() ?? string.Empty;
                    if (IsProgressText(text)) Report(host.Owner, key, text, ParsePercent(text));
                    else Complete(host.Owner, key);
                }
                label.TextChanged += Changed;
                Changed(null, EventArgs.Empty);
            }
        }

        private static bool IsInside(Control control, Control possibleParent)
        {
            for (Control? p = control.Parent; p is not null; p = p.Parent)
                if (ReferenceEquals(p, possibleParent)) return true;
            return false;
        }

        private static bool IsProgressText(string text)
        {
            // A real activity message must begin with "Đang ...". This explicitly excludes
            // static state such as "Sẵn sàng gửi — hiện không có USER đang giữ quyền nhập".
            if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("Đang ", StringComparison.OrdinalIgnoreCase)) return false;
            var value = text.ToLowerInvariant();
            return value.Contains("đồng bộ") || value.Contains("gửi") || value.Contains("tải") ||
                   value.Contains("ghi") || value.Contains("xuất") || value.Contains("nhận") ||
                   value.Contains("cập nhật") || value.Contains("khôi phục") || value.Contains("đẩy") ||
                   value.Contains("nhập");
        }

        private static int? ParsePercent(string text)
        {
            var explicitPercent = PercentRegex.Match(text);
            if (explicitPercent.Success && int.TryParse(explicitPercent.Groups["percent"].Value, out var parsed))
                return Math.Clamp(parsed, 0, 100);

            var match = FractionRegex.Match(text);
            if (!match.Success) return null;
            if (!TryDigits(match.Groups["done"].Value, out var done) || !TryDigits(match.Groups["total"].Value, out var total) || total <= 0) return null;
            return Math.Clamp((int)Math.Round(done * 100d / total), 0, 100);
        }

        private static bool TryDigits(string text, out long value)
            => long.TryParse(new string(text.Where(char.IsDigit).ToArray()), out value);

        private static string CleanMessage(string text)
        {
            var value = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length <= 105 ? value : value[..102] + "...";
        }

        private static void Rebuild(Host host)
        {
            host.Stack.SuspendLayout();
            try
            {
                foreach (Control child in host.Stack.Controls.Cast<Control>().ToArray()) child.Dispose();
                host.Stack.Controls.Clear();

                var tasks = host.Tasks.Values.OrderByDescending(x => x.UpdatedUtc).Take(2).ToList();
                if (tasks.Count == 0)
                {
                    host.Surface.Visible = false;
                    return;
                }

                var title = new Label
                {
                    Text = "Đang xử lý",
                    Width = 312,
                    Height = 18,
                    Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                    ForeColor = Color.DimGray,
                    Margin = new Padding(0, 0, 0, 3)
                };
                host.Stack.Controls.Add(title);

                foreach (var task in tasks)
                {
                    var row = new Panel { Width = 312, Height = 38, Margin = new Padding(0, 0, 0, 3) };
                    var displayMessage = task.Percent.HasValue && !PercentRegex.IsMatch(task.Message)
                        ? $"{task.Message} • {task.Percent.Value}%"
                        : task.Message;
                    var label = new Label
                    {
                        Text = displayMessage,
                        Dock = DockStyle.Top,
                        Height = 24,
                        AutoEllipsis = true,
                        Font = new Font("Segoe UI", 8.5F)
                    };
                    var bar = new ProgressBar { Dock = DockStyle.Bottom, Height = 8 };
                    if (task.Percent.HasValue)
                    {
                        bar.Style = ProgressBarStyle.Continuous;
                        bar.Value = task.Percent.Value;
                    }
                    else
                    {
                        bar.Style = ProgressBarStyle.Marquee;
                        bar.MarqueeAnimationSpeed = 28;
                    }
                    row.Controls.Add(label);
                    row.Controls.Add(bar);
                    host.Stack.Controls.Add(row);
                }

                host.Surface.Height = 29 + tasks.Count * 41;
                host.Surface.Visible = true;
                host.Surface.BringToFront();
                Reposition(host);
            }
            finally
            {
                host.Stack.ResumeLayout(true);
            }
        }

        private static void Reposition(Host host)
        {
            if (host.Owner.IsDisposed || host.Surface.IsDisposed) return;
            host.Surface.Left = Math.Max(8, host.Owner.ClientSize.Width - host.Surface.Width - 10);
            host.Surface.Top = Math.Max(8, host.Owner.ClientSize.Height - host.Surface.Height - 10);
            host.Surface.BringToFront();
        }
    }

    private static class NetworkRecoveryCoordinator
    {
        public static void Attach(Form form)
        {
            CancellationTokenSource? debounce = null;
            var recoveryGate = new SemaphoreSlim(1, 1);

            void Schedule()
            {
                if (form.IsDisposed) return;
                var next = new CancellationTokenSource();
                var previous = Interlocked.Exchange(ref debounce, next);
                try { previous?.Cancel(); } catch { }
                previous?.Dispose();
                _ = RecoverAfterChangeAsync(form, recoveryGate, next.Token);
            }

            NetworkHttpClientFactory.NetworkChanged += Schedule;
            form.FormClosed += (_, _) =>
            {
                NetworkHttpClientFactory.NetworkChanged -= Schedule;
                try { debounce?.Cancel(); } catch { }
                debounce?.Dispose();
                recoveryGate.Dispose();
            };
        }

        private static async Task RecoverAfterChangeAsync(Form form, SemaphoreSlim gate, CancellationToken ct)
        {
            try
            {
                await Task.Delay(1500, ct);
                await gate.WaitAsync(ct);
                try
                {
                    var session = AppSession.Current;
                    if (session is null || session.OfflineMode || !RuntimeConfigService.IsGoogleGatewayConfigured) return;

                    Exception? last = null;
                    var delays = new[] { 0, 2500, 6000 };
                    for (var attempt = 0; attempt < delays.Length; attempt++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (delays[attempt] > 0) await Task.Delay(delays[attempt], ct);
                        if (!ReferenceEquals(AppSession.Current, session)) return;

                        try
                        {
                            TaskProgressCenter.Report(form, "network-recovery", $"Đang khôi phục kết nối sau khi đổi mạng ({attempt + 1}/{delays.Length})...", (attempt * 100) / delays.Length);
                            await GoogleService.VerifyBindingAsync(writeHeaders: false);

                            var progress = new Progress<string>(message => TaskProgressCenter.Report(form, "network-recovery", message));
                            var canWrite = session.Profile.IsAdmin || session.Profile.HasPermission("sync_google") || session.Profile.HasPermission("damage_entry");
                            var canRead = canWrite || session.Profile.HasPermission("view_reports");
                            var hasPendingWrites = canWrite && (Database.GetPendingReports().Any() || (session.Profile.IsAdmin && SyncCacheStore.ProductsDirty));

                            // Most Windows network notifications do not mean business data changed.
                            // Avoid the heavier two-pass SyncNow path unless this laptop actually has local writes waiting.
                            if (hasPendingWrites)
                                await CloudSyncService.SyncNowAsync(progress, ct);
                            else if (canRead)
                                await CloudSyncService.PullReportsOnlyAsync(progress, TimeSpan.FromSeconds(30), ct);

                            BackgroundSyncCoordinator.EnqueuePending();
                            AppLog.TriggerAutoUpload();
                            TaskProgressCenter.Complete(form, "network-recovery");
                            AppLog.Info("NETWORK_RECOVERY_OK", "Đã khôi phục kết nối Google sau khi Windows đổi mạng.");
                            return;
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            last = ex;
                            AppLog.Warning("NETWORK_RECOVERY_RETRY", ex.Message, new Dictionary<string, object?>
                            {
                                ["attempt"] = attempt + 1,
                                ["error_type"] = ex.GetType().Name
                            });
                        }
                    }

                    TaskProgressCenter.Complete(form, "network-recovery");
                    if (last is not null)
                    {
                        NotificationCenter.Show(form,
                            "Đã chuyển mạng nhưng Google/Firebase chưa ổn định. Dữ liệu local vẫn được giữ an toàn; ứng dụng sẽ tiếp tục đồng bộ ở lần thao tác tiếp theo. " + NetworkHttpClientFactory.Friendly(last, "Google"),
                            "Kết nối sau khi đổi mạng",
                            MessageBoxIcon.Warning);
                    }
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                TaskProgressCenter.Complete(form, "network-recovery");
            }
            catch (ObjectDisposedException)
            {
                // Form is closing.
            }
        }
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }
}
