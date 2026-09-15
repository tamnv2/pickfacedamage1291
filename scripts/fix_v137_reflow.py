from pathlib import Path

p = Path('src/PickfaceDamage1291/V133Runtime.cs')
text = p.read_text(encoding='utf-8')
old = '''        try
        {
            flow.BeginInvoke((Action)(() =>
            {
                state.Pending = false;
                if (flow.IsDisposed) return;

                flow.PerformLayout();
                var bottom = flow.Padding.Top;
                foreach (Control child in flow.Controls)
                {
                    if (!child.Visible) continue;
                    bottom = Math.Max(bottom, child.Bottom + child.Margin.Bottom);
                }

                var desired = Math.Max(1, bottom + flow.Padding.Bottom + 2);
                if (flow.Height != desired)
                    flow.Height = desired;
                flow.Parent?.PerformLayout();
            }));
        }
        catch
        {
            state.Pending = false;
        }
'''
new = '''        try
        {
            flow.BeginInvoke((Action)(() =>
            {
                if (flow.IsDisposed)
                {
                    state.Pending = false;
                    return;
                }

                try
                {
                    // Keep Pending=true for the whole reflow. PerformLayout/Height changes can fire
                    // Layout again; those nested events must not enqueue an endless BeginInvoke loop.
                    flow.PerformLayout();
                    var bottom = flow.Padding.Top;
                    foreach (Control child in flow.Controls)
                    {
                        if (!child.Visible) continue;
                        bottom = Math.Max(bottom, child.Bottom + child.Margin.Bottom);
                    }

                    var desired = Math.Max(1, bottom + flow.Padding.Bottom + 2);
                    if (flow.Height != desired)
                        flow.Height = desired;
                    flow.Parent?.PerformLayout();
                }
                finally
                {
                    state.Pending = false;
                }
            }));
        }
        catch
        {
            state.Pending = false;
        }
'''
if old not in text:
    raise SystemExit('reflow block not found')
p.write_text(text.replace(old, new, 1), encoding='utf-8', newline='\n')
print('responsive flow reentrancy guard fixed')
