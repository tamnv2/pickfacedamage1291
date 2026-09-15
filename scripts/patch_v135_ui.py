from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    if old not in text:
        raise SystemExit(f"Expected block not found in {path}: {old[:160]!r}")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one block in {path}, found {count}")
    write(path, text.replace(old, new, 1))


# 1) Section 4 uses the same two-column form language as the other entry sections.
replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''    private GroupBox BuildSubmitSection()\n    {\n        var box = NewSection("4. Lưu và đồng bộ");\n        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(12), ColumnCount = 1, RowCount = 2 };\n        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));\n        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));\n        _send.Text = "GỬI THÔNG TIN HƯ HỎNG";\n        _send.Height = 42;\n        _send.Dock = DockStyle.Top;\n        _send.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);\n        _send.Click += async (_, _) => await SaveAndSendAsync();\n        _entryStatus.AutoSize = true;\n        _entryStatus.MaximumSize = new Size(1000, 0);\n        _entryStatus.Text = "Dữ liệu được lưu trên laptop trước. Khi mất mạng hoặc Google tạm lỗi, phiếu được giữ local để đồng bộ lại khi online.";\n        _entryStatus.Padding = new Padding(0, 8, 0, 4);\n        layout.Controls.Add(_send, 0, 0);\n        layout.Controls.Add(_entryStatus, 0, 1);\n        box.Controls.Add(layout);\n        return box;\n    }\n''',
    '''    private GroupBox BuildSubmitSection()\n    {\n        var box = NewSection("4. Lưu và đồng bộ");\n        var form = NewFormTable(190);\n\n        _send.Text = "Gửi thông tin hư hỏng";\n        _send.AutoSize = true;\n        _send.AutoSizeMode = AutoSizeMode.GrowAndShrink;\n        _send.MinimumSize = new Size(0, 40);\n        _send.Padding = new Padding(14, 4, 14, 4);\n        _send.Click += async (_, _) => await SaveAndSendAsync();\n\n        _entryStatus.AutoSize = true;\n        _entryStatus.MaximumSize = new Size(1000, 0);\n        _entryStatus.Text = "Dữ liệu được lưu trên máy trước và tự đồng bộ khi kết nối sẵn sàng.";\n        _entryStatus.Padding = new Padding(0, 7, 0, 7);\n\n        AddFormRow(form, "Thao tác", _send);\n        AddFormRow(form, "Trạng thái", _entryStatus);\n        box.Controls.Add(form);\n        return box;\n    }\n'''
)

# Image action bars must grow with DPI/font instead of living in a fixed-height row.
replace_once(
    "src/PickfaceDamage1291/MainFormV3.cs",
    '''        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));\n        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));\n        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };\n''',
    '''        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));\n        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));\n        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true };\n'''
)

# 2) Background-submit replacement must preserve the compact, standard button layout.
replace_once(
    "src/PickfaceDamage1291/V132Runtime.cs",
    '''        var replacement = new Button\n        {\n            Text = "GỬI THÔNG TIN HƯ HỎNG",\n            Height = Math.Max(42, oldButton.Height),\n            Dock = DockStyle.Top,\n            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),\n            Margin = oldButton.Margin,\n            Tag = "v132-submit"\n        };\n''',
    '''        var replacement = new Button\n        {\n            Text = "Gửi thông tin hư hỏng",\n            AutoSize = true,\n            AutoSizeMode = AutoSizeMode.GrowAndShrink,\n            MinimumSize = new Size(0, 40),\n            Dock = oldButton.Dock,\n            Anchor = oldButton.Anchor,\n            Padding = new Padding(14, 4, 14, 4),\n            Margin = oldButton.Margin,\n            Tag = "v132-submit"\n        };\n'''
)

# 3) One button design system: same font, padding and minimum height. AutoSize prevents text clipping under DPI scaling.
replace_once(
    "src/PickfaceDamage1291/V133Runtime.cs",
    '''    public static void StyleButton(Button button, ButtonVisual visual)\n    {\n        button.UseVisualStyleBackColor = false;\n        button.FlatStyle = FlatStyle.Flat;\n        button.FlatAppearance.BorderSize = 1;\n        button.Font = new Font("Segoe UI Semibold", Math.Max(9F, button.Font.Size), FontStyle.Bold);\n        button.Cursor = Cursors.Hand;\n\n        switch (visual)\n''',
    '''    public static void StyleButton(Button button, ButtonVisual visual)\n    {\n        button.UseVisualStyleBackColor = false;\n        button.FlatStyle = FlatStyle.Flat;\n        button.FlatAppearance.BorderSize = 1;\n        button.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);\n        button.Cursor = Cursors.Hand;\n        button.TextAlign = ContentAlignment.MiddleCenter;\n        button.AutoEllipsis = false;\n\n        // All main UI buttons share one geometry. AutoSize + a minimum height is DPI-safe:\n        // text can grow when Windows scaling requires it, but buttons never collapse below 40 px.\n        if (button.Dock != DockStyle.Fill)\n        {\n            button.AutoSize = true;\n            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;\n        }\n        button.Padding = new Padding(14, 5, 14, 5);\n        button.Margin = new Padding(4, 4, 8, 4);\n        button.MinimumSize = new Size(button.MinimumSize.Width, 40);\n\n        switch (visual)\n'''
)

# 4) The rebuilt image pane had its own fixed 48 px action row; make that responsive too.
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    '''        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));\n        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));\n        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));\n\n        actions.Dock = DockStyle.Fill;\n        actions.AutoSize = false;\n        actions.WrapContents = true;\n''',
    '''        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));\n        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));\n        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));\n\n        actions.Dock = DockStyle.Top;\n        actions.AutoSize = true;\n        actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;\n        actions.WrapContents = true;\n'''
)

# 5) History toolbar was the clearest clipping source: it wrapped inside a fixed 128 px row + AutoScroll.
replace_once(
    "src/PickfaceDamage1291/AuditControl.cs",
    '''        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };\n        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));\n        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));\n\n        var box = new GroupBox { Text = "Lịch sử thao tác hệ thống", Dock = DockStyle.Fill, Padding = new Padding(12) };\n        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };\n''',
    '''        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };\n        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));\n        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));\n\n        var box = new GroupBox\n        {\n            Text = "Lịch sử thao tác hệ thống",\n            Dock = DockStyle.Top,\n            AutoSize = true,\n            AutoSizeMode = AutoSizeMode.GrowAndShrink,\n            Padding = new Padding(12)\n        };\n        var tools = new FlowLayoutPanel\n        {\n            Dock = DockStyle.Top,\n            AutoSize = true,\n            AutoSizeMode = AutoSizeMode.GrowAndShrink,\n            WrapContents = true,\n            AutoScroll = false\n        };\n'''
)

# 6) User-facing Section 4 status must describe the state, not expose internal active_operator terminology.
replacements = {
    'ApplyState(session.OfflineMode ? "OFFLINE — đang trong thời hạn lease dự phòng 30 phút." : "Online — tài khoản đang giữ quyền nhập duy nhất.", manager.CanCreateDamage);':
    'ApplyState(session.OfflineMode ? "Offline — đang sử dụng quyền nhập dự phòng trong 30 phút." : "Sẵn sàng gửi — tài khoản đang giữ quyền nhập.", manager.CanCreateDamage);',
    'if (status is not null) status.Text = "ADMIN đang offline: không thể xác minh active_operator nên chức năng tạo phiếu bị khóa an toàn.";':
    'if (status is not null) status.Text = "Tạm khóa gửi — ADMIN đang offline nên chưa thể xác minh quyền nhập.";',
    'status.Text = free ? "ADMIN: hiện không có USER giữ quyền nhập. ADMIN có thể nhập; hệ thống sẽ khóa ngay khi USER nhận active_operator." : $"ADMIN: USER {snapshot.Value!.Username} đang giữ quyền nhập. Chỉ xem, không được tạo phiếu.";':
    'status.Text = free ? "Sẵn sàng gửi — hiện không có USER đang giữ quyền nhập." : $"Tạm khóa gửi — USER {snapshot.Value!.Username} đang giữ quyền nhập.";',
    'if (status is not null) status.Text = "Không xác minh được active_operator — khóa Gửi để tránh hai người nhập cùng lúc.";':
    'if (status is not null) status.Text = "Tạm khóa gửi — chưa xác minh được quyền nhập. Hãy kiểm tra kết nối.";'
}
text = read("src/PickfaceDamage1291/SessionUiBinder.cs")
for old, new in replacements.items():
    if old not in text:
        raise SystemExit(f"Expected SessionUiBinder text not found: {old}")
    text = text.replace(old, new, 1)
write("src/PickfaceDamage1291/SessionUiBinder.cs", text)

# 7) Release metadata.
replace_once(
    "src/PickfaceDamage1291/PickfaceDamage1291.csproj",
    '''    <Version>1.3.4</Version>\n    <AssemblyVersion>1.3.4.0</AssemblyVersion>\n    <FileVersion>1.3.4.0</FileVersion>\n''',
    '''    <Version>1.3.5</Version>\n    <AssemblyVersion>1.3.5.0</AssemblyVersion>\n    <FileVersion>1.3.5.0</FileVersion>\n'''
)

print("v1.3.5 UI consistency patch applied")
