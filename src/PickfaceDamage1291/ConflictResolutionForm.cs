namespace PickfaceDamage1291;

internal sealed class ConflictResolutionForm : Form
{
    private readonly List<ProductConflict> _conflicts;
    private readonly DataGridView _grid = new();

    public ConflictResolutionForm(List<ProductConflict> conflicts)
    {
        _conflicts = conflicts;
        Text = "Xử lý SKU thay đổi";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1100;
        Height = 620;
        MinimumSize = new Size(850, 450);

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(12, 10, 12, 6),
            Text = "SKU đã tồn tại nhưng Tên sản phẩm hoặc Base Units thay đổi. Mặc định GIỮ DỮ LIỆU CŨ; chỉ tích chọn khi muốn dùng dữ liệu mới."
        };

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Sku", HeaderText = "SKU", FillWeight = 60, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OldName", HeaderText = "Tên hiện tại", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "NewName", HeaderText = "Tên file mới", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OldBase", HeaderText = "Base hiện tại", FillWeight = 60, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "NewBase", HeaderText = "Base mới", FillWeight = 60, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "UseNew", HeaderText = "Dùng dữ liệu mới", FillWeight = 60 });

        foreach (var c in conflicts)
            _grid.Rows.Add(c.Sku, c.OldName, c.NewName, c.OldBaseUnit, c.NewBaseUnit, false);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var ok = new Button { Text = "Tiếp tục cập nhật", AutoSize = true, Height = 34 };
        var cancel = new Button { Text = "Huỷ", AutoSize = true, Height = 34 };
        var useAll = new Button { Text = "Dùng mới cho tất cả", AutoSize = true, Height = 34 };
        var keepAll = new Button { Text = "Giữ cũ cho tất cả", AutoSize = true, Height = 34 };
        ok.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        useAll.Click += (_, _) => SetAll(true);
        keepAll.Click += (_, _) => SetAll(false);
        buttons.Controls.AddRange([ok, cancel, useAll, keepAll]);

        Controls.Add(_grid);
        Controls.Add(info);
        Controls.Add(buttons);
    }

    private void SetAll(bool value)
    {
        foreach (DataGridViewRow row in _grid.Rows) row.Cells["UseNew"].Value = value;
    }

    private void SaveAndClose()
    {
        _grid.EndEdit();
        for (var i = 0; i < _conflicts.Count; i++)
            _conflicts[i].UseNew = Convert.ToBoolean(_grid.Rows[i].Cells["UseNew"].Value ?? false);
        DialogResult = DialogResult.OK;
        Close();
    }
}
