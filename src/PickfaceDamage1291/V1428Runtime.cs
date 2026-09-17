using System.Reflection;
using System.Runtime.InteropServices;

namespace PickfaceDamage1291;

internal static class V1428Runtime
{
    private const int WmSetRedraw = 0x000B;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private static readonly PropertyInfo? DoubleBufferedProperty = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Apply(MainForm form)
    {
        EnableDoubleBufferingRecursive(form);
        var tabs = FindFirst<TabControl>(form);
        if (tabs is null)
        {
            AppLog.Warning("V1428_TABCONTROL_NOT_FOUND", "Không tìm thấy TabControl để áp dụng chống nhấp nháy/chuyển trang.");
            return;
        }

        tabs.Selecting += (_, _) => FreezeUntilSelectionSettles(tabs);
        AppLog.Info("V1428_RUNTIME_APPLIED", "Đã áp dụng chuyển tab không vẽ trung gian và double-buffering v1.4.28.");
    }

    private static void FreezeUntilSelectionSettles(TabControl tabs)
    {
        if (!tabs.IsHandleCreated) return;
        SendMessage(tabs.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        try
        {
            tabs.BeginInvoke(new Action(() =>
            {
                if (tabs.IsDisposed || !tabs.IsHandleCreated) return;
                SendMessage(tabs.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                RedrawWindow(tabs.Handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwAllChildren | RdwUpdateNow);
            }));
        }
        catch
        {
            if (!tabs.IsDisposed && tabs.IsHandleCreated)
            {
                SendMessage(tabs.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                tabs.Invalidate(true);
            }
        }
    }

    private static void EnableDoubleBufferingRecursive(Control control)
    {
        try
        {
            if (control is DataGridView or TabControl or TableLayoutPanel or FlowLayoutPanel or Panel)
                DoubleBufferedProperty?.SetValue(control, true);
        }
        catch
        {
            // Cosmetic optimization only; never block business UI if a control rejects reflection.
        }

        foreach (Control child in control.Controls)
            EnableDoubleBufferingRecursive(child);
    }

    private static T? FindFirst<T>(Control root) where T : Control
    {
        if (root is T match) return match;
        foreach (Control child in root.Controls)
        {
            var found = FindFirst<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr updateRect, IntPtr updateRegion, uint flags);
}
