namespace PickfaceDamage1291;

internal static class UiRuntimeFixes
{
    private static bool _googleBootstrapRunning;

    public static void Attach(Form form)
    {
        void Reflow()
        {
            if (form.IsDisposed) return;
            foreach (var flow in FindAll<FlowLayoutPanel>(form)
                         .Where(x => x.AutoScroll && x.FlowDirection == FlowDirection.TopDown && !x.WrapContents))
            {
                FixVerticalStackWidth(flow);
            }
        }

        form.Load += (_, _) => form.BeginInvoke((Action)Reflow);
        form.Resize += (_, _) =>
        {
            if (form.IsHandleCreated && !form.IsDisposed)
                form.BeginInvoke((Action)Reflow);
        };
        form.Shown += async (_, _) =>
        {
            Reflow();
            await BootstrapGoogleAfterLoginAsync(form);
            Reflow();
        };
    }

    private static void FixVerticalStackWidth(FlowLayoutPanel panel)
    {
        if (panel.IsDisposed) return;

        var width = panel.ClientSize.Width
                    - panel.Padding.Horizontal
                    - SystemInformation.VerticalScrollBarWidth
                    - 12;
        if (width < 520) width = 520;

        panel.SuspendLayout();
        try
        {
            foreach (Control child in panel.Controls)
            {
                // AutoSize + GrowAndShrink inside a vertical FlowLayoutPanel can collapse
                // GroupBox/TableLayout widths to their preferred minimum on some DPI setups.
                // Fix only the width; height remains content-driven.
                child.MinimumSize = new Size(width, 0);
                child.MaximumSize = new Size(width, 0);
                child.Width = width;

                if (child is GroupBox box)
                {
                    box.AutoSize = true;
                    box.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                }
            }
        }
        finally
        {
            panel.ResumeLayout(true);
        }
    }

    private static async Task BootstrapGoogleAfterLoginAsync(Form form)
    {
        if (_googleBootstrapRunning || form.IsDisposed) return;
        var session = AppSession.Current;
        if (session is null) return;

        if (session.OfflineMode)
        {
            SetGoogleHeader(form, "Google: offline/local");
            return;
        }

        if (!GoogleService.IsClientConfigured())
        {
            SetGoogleHeader(form, "Google: thiếu cấu hình OAuth");
            return;
        }

        _googleBootstrapRunning = true;
        try
        {
            SetGoogleHeader(form, "Google: đang kiểm tra...");

            if (GoogleService.IsConnected())
            {
                // Existing long-lived token: refresh/verify silently. Normal logins do not
                // require the user to press a separate Google-connect button.
                await GoogleService.VerifyBindingAsync();
            }
            else if (session.Profile.IsAdmin)
            {
                // A Firebase email/password login cannot itself grant Google Drive scope.
                // ADMIN gets the one-time browser consent automatically on a fresh/revoked
                // machine; after that the refresh token is reused silently.
                SetGoogleHeader(form, "Google: cấp quyền lần đầu...");
                await GoogleService.AuthorizeAsync();
            }
            else
            {
                // USER must never be blocked from the local-first workflow because the
                // shared laptop has not yet been provisioned by ADMIN.
                SetGoogleHeader(form, "Google: chờ ADMIN thiết lập");
                return;
            }

            SetGoogleHeader(form, "Google: đã kết nối");

            if (session.Profile.IsAdmin || session.Profile.HasPermission("sync_google"))
            {
                try
                {
                    await GoogleService.SyncAllPendingAsync();
                }
                catch
                {
                    // Local-first invariant: pending reports remain local and can retry later.
                }
            }
        }
        catch (Exception ex)
        {
            SetGoogleHeader(form, "Google: local/chưa sẵn sàng");
            if (session.Profile.IsAdmin && !form.IsDisposed)
            {
                MessageBox.Show(
                    form,
                    "Đăng nhập ứng dụng đã thành công và vẫn có thể làm việc local.\n\n" +
                    "Google Drive/Sheet chưa sẵn sàng. Trên máy mới hoặc khi quyền Google bị thu hồi, trình duyệt cần cấp quyền một lần; sau đó ứng dụng tự dùng lại và tự refresh, không phải bấm Kết nối Google mỗi lần.\n\n" +
                    ex.Message,
                    "Google chưa sẵn sàng",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _googleBootstrapRunning = false;
        }
    }

    private static void SetGoogleHeader(Control root, string text)
    {
        foreach (var label in FindAll<Label>(root))
        {
            if (label.Text.StartsWith("Google:", StringComparison.OrdinalIgnoreCase))
            {
                label.Text = text;
                return;
            }
        }
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child))
            yield return found;
    }
}
