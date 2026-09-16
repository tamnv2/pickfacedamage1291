using System.Reflection;

namespace PickfaceDamage1291;

internal static class V1414Runtime
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".heic", ".webp"];

    public static void Apply(MainForm main)
    {
        try
        {
            ReplaceQuantityStepper(main);
            EnableClipboardImagePaste(main);
            AppLog.Info("V1414_RUNTIME_APPLIED", "Đã áp dụng input số lượng dạng nhập số và dán ảnh từ clipboard.");
        }
        catch (Exception ex)
        {
            AppLog.Exception("V1414_RUNTIME_APPLY_FAILED", ex);
        }
    }

    private static void ReplaceQuantityStepper(MainForm main)
    {
        var quantity = Field<NumericUpDown>(main, "_quantity");
        if (quantity.Tag as string == "v1414-hidden-quantity") return;
        if (quantity.Parent is not TableLayoutPanel table) return;

        var pos = table.GetPositionFromControl(quantity);
        var colSpan = table.GetColumnSpan(quantity);
        var rowSpan = table.GetRowSpan(quantity);
        var margin = quantity.Margin;
        var dock = quantity.Dock;
        var anchor = quantity.Anchor;
        var font = quantity.Font;

        quantity.Minimum = 0;
        quantity.Tag = "v1414-hidden-quantity";
        table.Controls.Remove(quantity);
        quantity.Visible = false;

        var input = new TextBox
        {
            Text = quantity.Value >= 1 ? decimal.Truncate(quantity.Value).ToString("0") : string.Empty,
            MaxLength = 9,
            Margin = margin,
            Dock = dock,
            Anchor = anchor,
            Font = font,
            Tag = "v1414-quantity-input"
        };

        var syncing = false;

        void SyncNumericFromText()
        {
            if (syncing) return;
            syncing = true;
            try
            {
                var digits = new string(input.Text.Where(char.IsDigit).Take(9).ToArray());
                if (!string.Equals(digits, input.Text, StringComparison.Ordinal))
                {
                    input.Text = digits;
                    input.SelectionStart = input.TextLength;
                }

                if (digits.Length == 0)
                {
                    if (quantity.Value != 0) quantity.Value = 0;
                    return;
                }

                if (!decimal.TryParse(digits, out var value)) value = 0;
                value = Math.Clamp(value, 0, quantity.Maximum);
                if (quantity.Value != value) quantity.Value = value;
            }
            finally
            {
                syncing = false;
            }
        }

        input.TextChanged += (_, _) => SyncNumericFromText();
        input.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        };
        input.Leave += (_, _) =>
        {
            if (quantity.Value > 0)
            {
                syncing = true;
                input.Text = decimal.Truncate(quantity.Value).ToString("0");
                syncing = false;
            }
        };
        quantity.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            input.Text = quantity.Value >= 1 ? decimal.Truncate(quantity.Value).ToString("0") : string.Empty;
            input.SelectionStart = input.TextLength;
            syncing = false;
        };

        table.Controls.Add(input, pos.Column, pos.Row);
        table.SetColumnSpan(input, colSpan);
        table.SetRowSpan(input, rowSpan);
    }

    private static void EnableClipboardImagePaste(MainForm main)
    {
        var tabs = Field<TabControl>(main, "_tabs");
        var entryTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault(x => string.Equals(x.Text, "Nhập hư hỏng", StringComparison.Ordinal));
        if (entryTab is null) return;

        var imageBox = FindAll<GroupBox>(entryTab)
            .FirstOrDefault(x => x.Text.Contains("Hình ảnh hiện trạng", StringComparison.OrdinalIgnoreCase));
        if (imageBox is null) return;

        imageBox.Text = "3. Hình ảnh hiện trạng — tối đa 5 ảnh — hỗ trợ Ctrl+V";

        var actions = FindAll<FlowLayoutPanel>(imageBox)
            .FirstOrDefault(x => x.Controls.OfType<Button>().Any(b => b.Text.Contains("Thêm ảnh", StringComparison.OrdinalIgnoreCase)));
        if (actions is not null && !actions.Controls.OfType<Button>().Any(x => x.Tag as string == "v1414-paste-image"))
        {
            var paste = new Button
            {
                Text = "Dán ảnh (Ctrl+V)",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(0, 42),
                Padding = new Padding(14, 6, 14, 6),
                Margin = new Padding(4, 4, 8, 4),
                Tag = "v1414-paste-image"
            };
            AppUiStyle.StyleButton(paste, ButtonVisual.Normal);
            paste.Click += (_, _) => PasteClipboardImages(main);
            actions.Controls.Add(paste);

            var add = actions.Controls.OfType<Button>()
                .FirstOrDefault(x => x.Text.Contains("Thêm ảnh", StringComparison.OrdinalIgnoreCase));
            if (add is not null)
                actions.Controls.SetChildIndex(paste, Math.Min(actions.Controls.Count - 1, actions.Controls.GetChildIndex(add) + 1));
        }

        main.KeyPreview = true;
        main.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.V || !e.Control || !ReferenceEquals(tabs.SelectedTab, entryTab)) return;
            if (!ClipboardHasImagePayload()) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            PasteClipboardImages(main);
        };
    }

    private static bool ClipboardHasImagePayload()
    {
        try
        {
            if (Clipboard.ContainsImage()) return true;
            if (!Clipboard.ContainsFileDropList()) return false;
            return Clipboard.GetFileDropList().Cast<string>().Any(IsSupportedImageFile);
        }
        catch
        {
            return false;
        }
    }

    private static void PasteClipboardImages(MainForm main)
    {
        var draftImages = Field<List<string>>(main, "_draftImages");
        var draftHashes = Field<HashSet<string>>(main, "_draftImageHashes");
        if (draftImages.Count >= 5)
        {
            NotificationCenter.Show(main, "Mỗi phiếu chỉ cho phép tối đa 5 ảnh.", "Giới hạn ảnh", MessageBoxIcon.Information);
            return;
        }

        var added = 0;
        try
        {
            if (Clipboard.ContainsImage())
            {
                using var source = Clipboard.GetImage();
                if (source is not null && AddClipboardBitmap(main, source, draftImages, draftHashes)) added++;
            }
            else if (Clipboard.ContainsFileDropList())
            {
                foreach (var source in Clipboard.GetFileDropList().Cast<string>().Where(IsSupportedImageFile))
                {
                    if (draftImages.Count >= 5) break;
                    if (AddImageFile(main, source, draftImages, draftHashes)) added++;
                }
            }
            else
            {
                NotificationCenter.Show(main, "Clipboard hiện không có ảnh để dán.", "Dán ảnh", MessageBoxIcon.Information);
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Exception("CLIPBOARD_IMAGE_PASTE_FAILED", ex);
            NotificationCenter.Show(main, "Không đọc được ảnh từ clipboard: " + ex.Message, "Dán ảnh thất bại", MessageBoxIcon.Warning);
            return;
        }

        if (added > 0)
        {
            InvokePrivate(main, "RefreshImageList");
            AppLog.Info("CLIPBOARD_IMAGE_PASTED", "Đã thêm ảnh từ clipboard vào phiếu.", new Dictionary<string, object?>
            {
                ["added"] = added,
                ["total"] = draftImages.Count
            });
            NotificationCenter.Show(main, $"Đã thêm {added} ảnh từ clipboard.", "Dán ảnh", MessageBoxIcon.Information);
        }
    }

    private static bool AddClipboardBitmap(MainForm main, Image source, List<string> draftImages, HashSet<string> draftHashes)
    {
        if (draftImages.Count >= 5) return false;
        var draftId = Convert.ToString(GetFieldValue(main, "_draftId")) ?? Guid.NewGuid().ToString("D");
        var folder = AppPaths.GetDraftImageFolder(draftId);
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $"clipboard_{Guid.NewGuid():N}.png");
        try
        {
            using var bitmap = new Bitmap(source);
            bitmap.Save(temp, System.Drawing.Imaging.ImageFormat.Png);
            var hash = ImageHashService.Sha256File(temp);
            if (!draftHashes.Add(hash))
            {
                File.Delete(temp);
                NotificationCenter.Show(main, "Ảnh này đã có trong phiếu và được bỏ qua.", "Ảnh trùng", MessageBoxIcon.Information);
                return false;
            }

            var target = Path.Combine(folder, $"{Guid.NewGuid():N}_{hash[..12]}.png");
            File.Move(temp, target);
            draftImages.Add(target);
            return true;
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }

    private static bool AddImageFile(MainForm main, string source, List<string> draftImages, HashSet<string> draftHashes)
    {
        if (draftImages.Count >= 5 || !IsSupportedImageFile(source) || !File.Exists(source)) return false;
        var hash = ImageHashService.Sha256File(source);
        if (!draftHashes.Add(hash))
        {
            NotificationCenter.Show(main, $"Ảnh {Path.GetFileName(source)} đã có trong phiếu và được bỏ qua.", "Ảnh trùng", MessageBoxIcon.Information);
            return false;
        }

        var draftId = Convert.ToString(GetFieldValue(main, "_draftId")) ?? Guid.NewGuid().ToString("D");
        var folder = AppPaths.GetDraftImageFolder(draftId);
        Directory.CreateDirectory(folder);
        var ext = Path.GetExtension(source).ToLowerInvariant();
        var target = Path.Combine(folder, $"{Guid.NewGuid():N}_{hash[..12]}{ext}");
        File.Copy(source, target, false);
        draftImages.Add(target);
        return true;
    }

    private static bool IsSupportedImageFile(string path)
        => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static T Field<T>(object owner, string name) where T : class
        => owner.GetType().GetField(name, PrivateInstance)?.GetValue(owner) as T
           ?? throw new MissingFieldException(owner.GetType().Name, name);

    private static object? GetFieldValue(object owner, string name)
        => owner.GetType().GetField(name, PrivateInstance)?.GetValue(owner);

    private static void InvokePrivate(object owner, string name)
        => owner.GetType().GetMethod(name, PrivateInstance)?.Invoke(owner, null);

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        if (root is T self) yield return self;
        foreach (Control child in root.Controls)
        foreach (var found in FindAll<T>(child)) yield return found;
    }
}
