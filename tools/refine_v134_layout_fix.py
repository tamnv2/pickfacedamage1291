from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "src/PickfaceDamage1291/MainFormV3.cs"
text = path.read_text(encoding="utf-8")
old = '''    private static void ResizeFlowChildren(FlowLayoutPanel panel)\n    {\n        var width = Math.Max(260, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12);\n        var widthConstraint = new Size(width, 0);\n        foreach (Control child in panel.Controls)\n        {\n            child.MinimumSize = widthConstraint;\n            child.MaximumSize = widthConstraint;\n            child.Width = width;\n        }\n    }\n'''
new = '''    private static void ResizeFlowChildren(FlowLayoutPanel panel)\n    {\n        var width = Math.Max(520, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12);\n        foreach (Control child in panel.Controls) child.Width = width;\n    }\n'''
if text.count(old) != 1:
    raise SystemExit(f"Expected one MainForm block, found {text.count(old)}")
path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")
print("Refined v1.3.4: MainForm stays neutral; runtime owns dynamic width constraints")
