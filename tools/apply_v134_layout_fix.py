from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected one match in {path}, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


# Root cause fix: FlowLayoutPanel children are AutoSize GroupBox controls. v1.3.3 cleared
# MinimumSize/MaximumSize during every Reflow, allowing WinForms preferred-width calculation
# to collapse GroupBox width to only a few pixels. Keep the current width dynamically pinned
# while leaving height unconstrained so AutoSize still works vertically.
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    '''    private static void FixVerticalStackWidth(FlowLayoutPanel panel)\n    {\n        if (panel.IsDisposed) return;\n        var width = panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12;\n        if (width < 220) return;\n\n        panel.SuspendLayout();\n        try\n        {\n            foreach (Control child in panel.Controls)\n            {\n                // Do not pin MinimumSize/MaximumSize to stale values; that caused text to collapse after resize.\n                child.MinimumSize = Size.Empty;\n                child.MaximumSize = Size.Empty;\n                child.Width = width;\n                if (child is GroupBox box)\n                {\n                    box.AutoSize = true;\n                    box.AutoSizeMode = AutoSizeMode.GrowAndShrink;\n                }\n            }\n        }\n        finally\n        {\n            panel.ResumeLayout(true);\n        }\n    }\n''',
    '''    private static void FixVerticalStackWidth(FlowLayoutPanel panel)\n    {\n        if (panel.IsDisposed) return;\n\n        // FlowLayoutPanel + AutoSize GroupBox is unstable when only Width is assigned:\n        // WinForms may recalculate the preferred width back to a few pixels. Pin only the\n        // width (height stays 0/unbounded) and refresh the pin whenever the panel changes size.\n        var width = Math.Max(260,\n            panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12);\n\n        panel.SuspendLayout();\n        try\n        {\n            foreach (Control child in panel.Controls)\n            {\n                var widthConstraint = new Size(width, 0);\n                child.MinimumSize = widthConstraint;\n                child.MaximumSize = widthConstraint;\n                child.Width = width;\n                if (child is GroupBox box)\n                {\n                    box.AutoSize = true;\n                    box.AutoSizeMode = AutoSizeMode.GrowAndShrink;\n                }\n            }\n        }\n        finally\n        {\n            panel.ResumeLayout(true);\n        }\n    }\n'''
)

# Keep width constraints fresh when the entry splitter itself is moved/resized.
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    '''        split.HandleCreated += (_, _) => ApplyStableEntrySplit(split);\n        split.SizeChanged += (_, _) => ApplyStableEntrySplit(split);\n        _entryLayoutApplied = true;\n''',
    '''        split.HandleCreated += (_, _) =>\n        {\n            ApplyStableEntrySplit(split);\n            FixVerticalStackWidth(left);\n        };\n        split.SizeChanged += (_, _) =>\n        {\n            ApplyStableEntrySplit(split);\n            FixVerticalStackWidth(left);\n        };\n        split.SplitterMoved += (_, _) => FixVerticalStackWidth(left);\n        left.SizeChanged += (_, _) => FixVerticalStackWidth(left);\n        _entryLayoutApplied = true;\n'''
)

# Make the base form use the same rule from the very first layout pass (Settings and the
# initial Damage flow) so there is no visible collapsed frame before runtime hooks finish.
replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''    private static void ResizeFlowChildren(FlowLayoutPanel panel)\n    {\n        var width = Math.Max(520, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12);\n        foreach (Control child in panel.Controls) child.Width = width;\n    }\n''',
    '''    private static void ResizeFlowChildren(FlowLayoutPanel panel)\n    {\n        var width = Math.Max(260, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12);\n        var widthConstraint = new Size(width, 0);\n        foreach (Control child in panel.Controls)\n        {\n            child.MinimumSize = widthConstraint;\n            child.MaximumSize = widthConstraint;\n            child.Width = width;\n        }\n    }\n'''
)

# Version bump.
replace_once(
    "src/PickfaceDamage1291/PickfaceDamage1291.csproj",
    '''    <Version>1.3.3</Version>\n    <AssemblyVersion>1.3.3.0</AssemblyVersion>\n    <FileVersion>1.3.3.0</FileVersion>\n''',
    '''    <Version>1.3.4</Version>\n    <AssemblyVersion>1.3.4.0</AssemblyVersion>\n    <FileVersion>1.3.4.0</FileVersion>\n'''
)

print("v1.3.4 layout width stability patch applied")
