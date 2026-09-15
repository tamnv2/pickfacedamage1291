from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    if old not in text:
        raise SystemExit(f"Expected block not found in {path}: {old[:120]!r}")
    if text.count(old) != 1:
        raise SystemExit(f"Expected exactly one block in {path}, found {text.count(old)}")
    write(path, text.replace(old, new, 1))


# 1) Program: apply v1.3.3 presentation/input/account logic after the legacy UI builder.
replace_once(
    "src/PickfaceDamage1291/Program.cs",
    """            V132Runtime.Apply(main);\n            UiRuntimeFixes.Attach(main);\n            Application.Run(main);\n""",
    """            V132Runtime.Apply(main);\n            UiRuntimeFixes.Attach(main);\n            V133Runtime.Apply(main);\n            Application.Run(main);\n""",
)

# 2) V132: background sync/report details/storage remain, but it no longer owns entry layout.
replace_once(
    "src/PickfaceDamage1291/V132Runtime.cs",
    """        ReplaceSubmitButton(form);\n        AttachBackgroundProgress(form);\n        AttachReportDetails(form);\n        ApplyResponsiveEntryLayout(form);\n        CleanUserFacingPresentation(form);\n        AddStorageMoveAction(form);\n\n        form.Resize += (_, _) => ApplyResponsiveEntryLayout(form);\n        form.Load += (_, _) => SchedulePresentationRefresh(form);\n""",
    """        ReplaceSubmitButton(form);\n        AttachBackgroundProgress(form);\n        AttachReportDetails(form);\n        CleanUserFacingPresentation(form);\n        AddStorageMoveAction(form);\n\n        form.Load += (_, _) => SchedulePresentationRefresh(form);\n""",
)
replace_once(
    "src/PickfaceDamage1291/V132Runtime.cs",
    """            CleanUserFacingPresentation(form);\n            ApplyResponsiveEntryLayout(form);\n            AttachBackgroundProgress(form);\n            AddStorageMoveAction(form);\n""",
    """            CleanUserFacingPresentation(form);\n            AttachBackgroundProgress(form);\n            AddStorageMoveAction(form);\n""",
)

# 3) UiRuntimeFixesV2 becomes the single owner of the left/right entry workspace.
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    """    public static void Attach(Form form)\n    {\n        var tabs = FindAll<TabControl>(form).FirstOrDefault();\n""",
    """    public static void Attach(Form form)\n    {\n        // State is per MainForm instance. Logging out and signing in again must rebuild the workspace.\n        _entryLayoutApplied = false;\n        _updateUi = null;\n        _updateCheckRunning = false;\n\n        var tabs = FindAll<TabControl>(form).FirstOrDefault();\n""",
)
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    """        var split = new SplitContainer\n        {\n            Dock = DockStyle.Fill,\n            Orientation = Orientation.Vertical,\n            SplitterWidth = 7,\n            Panel1MinSize = 400,\n            Panel2MinSize = 480,\n            BackColor = SystemColors.ControlDark\n        };\n""",
    """        var split = new SplitContainer\n        {\n            Dock = DockStyle.Fill,\n            Orientation = Orientation.Vertical,\n            SplitterWidth = 7,\n            Panel1MinSize = 0,\n            Panel2MinSize = 0,\n            BackColor = SystemColors.ControlDark,\n            Tag = \"v133-entry-split\"\n        };\n""",
)
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    """        var left = new FlowLayoutPanel\n        {\n            Dock = DockStyle.Fill,\n            AutoScroll = true,\n            FlowDirection = FlowDirection.TopDown,\n            WrapContents = false,\n            Padding = new Padding(14),\n            BackColor = Color.WhiteSmoke\n        };\n""",
    """        var left = new FlowLayoutPanel\n        {\n            Dock = DockStyle.Fill,\n            AutoScroll = true,\n            FlowDirection = FlowDirection.TopDown,\n            WrapContents = false,\n            Padding = new Padding(14),\n            BackColor = Color.WhiteSmoke,\n            Tag = \"v133-entry-left\"\n        };\n""",
)
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    """        split.HandleCreated += (_, _) => SetInitialSplitter(split);\n        split.SizeChanged += (_, _) =>\n        {\n            if (split.Tag is null) SetInitialSplitter(split);\n        };\n        _entryLayoutApplied = true;\n    }\n\n    private static void SetInitialSplitter(SplitContainer split)\n    {\n        if (split.Width < 920) return;\n        var desired = (int)Math.Round(split.Width * 0.45);\n        desired = Math.Max(split.Panel1MinSize, Math.Min(desired, split.Width - split.Panel2MinSize - split.SplitterWidth));\n        if (desired > 0) split.SplitterDistance = desired;\n        split.Tag = \"initialized\";\n    }\n""",
    """        split.HandleCreated += (_, _) => ApplyStableEntrySplit(split);\n        split.SizeChanged += (_, _) => ApplyStableEntrySplit(split);\n        _entryLayoutApplied = true;\n    }\n\n    private static void ApplyStableEntrySplit(SplitContainer split)\n    {\n        if (split.IsDisposed || split.ClientSize.Width <= split.SplitterWidth + 240) return;\n        try\n        {\n            // Always keep information on the left and images on the right. Never switch to top/bottom.\n            split.Panel1MinSize = 0;\n            split.Panel2MinSize = 0;\n            if (split.Orientation != Orientation.Vertical) split.Orientation = Orientation.Vertical;\n\n            var available = Math.Max(1, split.ClientSize.Width - split.SplitterWidth);\n            var minimumPane = available >= 760 ? 320 : Math.Max(120, available / 3);\n            var preferred = (int)Math.Round(available * 0.45);\n            var desired = Math.Clamp(preferred, minimumPane, Math.Max(minimumPane, available - minimumPane));\n            if (desired > 0 && desired < available) split.SplitterDistance = desired;\n\n            var right = available - split.SplitterDistance;\n            split.Panel1MinSize = Math.Min(300, Math.Max(0, split.SplitterDistance));\n            split.Panel2MinSize = Math.Min(300, Math.Max(0, right));\n        }\n        catch (ArgumentOutOfRangeException)\n        {\n            split.Panel1MinSize = 0;\n            split.Panel2MinSize = 0;\n        }\n    }\n""",
)
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    """    private static void Reflow(Form form)\n    {\n        if (form.IsDisposed) return;\n        foreach (var flow in FindAll<FlowLayoutPanel>(form)\n                     .Where(x => x.AutoScroll && x.FlowDirection == FlowDirection.TopDown && !x.WrapContents))\n            FixVerticalStackWidth(flow);\n    }\n""",
    """    private static void Reflow(Form form)\n    {\n        if (form.IsDisposed) return;\n        foreach (var split in FindAll<SplitContainer>(form).Where(x => Equals(x.Tag, \"v133-entry-split\")))\n            ApplyStableEntrySplit(split);\n        foreach (var flow in FindAll<FlowLayoutPanel>(form)\n                     .Where(x => x.AutoScroll && x.FlowDirection == FlowDirection.TopDown && !x.WrapContents))\n            FixVerticalStackWidth(flow);\n    }\n""",
)
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    """    private static void FixVerticalStackWidth(FlowLayoutPanel panel)\n    {\n        if (panel.IsDisposed) return;\n        var width = panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12;\n        if (width < 360) width = 360;\n\n        panel.SuspendLayout();\n        try\n        {\n            foreach (Control child in panel.Controls)\n            {\n                child.MinimumSize = new Size(width, 0);\n                child.MaximumSize = new Size(width, 0);\n                child.Width = width;\n                if (child is GroupBox box)\n                {\n                    box.AutoSize = true;\n                    box.AutoSizeMode = AutoSizeMode.GrowAndShrink;\n                }\n            }\n        }\n        finally\n        {\n            panel.ResumeLayout(true);\n        }\n    }\n""",
    """    private static void FixVerticalStackWidth(FlowLayoutPanel panel)\n    {\n        if (panel.IsDisposed) return;\n        var width = panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12;\n        if (width < 220) return;\n\n        panel.SuspendLayout();\n        try\n        {\n            foreach (Control child in panel.Controls)\n            {\n                // Do not pin MinimumSize/MaximumSize to stale values; that caused text to collapse after resize.\n                child.MinimumSize = Size.Empty;\n                child.MaximumSize = Size.Empty;\n                child.Width = width;\n                if (child is GroupBox box)\n                {\n                    box.AutoSize = true;\n                    box.AutoSizeMode = AutoSizeMode.GrowAndShrink;\n                }\n            }\n        }\n        finally\n        {\n            panel.ResumeLayout(true);\n        }\n    }\n""",
)
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    """            RowCount = 4,\n            Padding = new Padding(12)\n""",
    """            RowCount = 5,\n            Padding = new Padding(12)\n""",
)
replace_once(
    "src/PickfaceDamage1291/UiRuntimeFixesV2.cs",
    """        AddRow(table, \"Phiên bản hiện tại\", current);\n        AddRow(table, \"Trạng thái\", status);\n        AddRow(table, \"File đang chạy\", location);\n        AddRow(table, \"Thao tác\", actions);\n""",
    """        AddRow(table, \"Phiên bản hiện tại\", current);\n        AddRow(table, \"Trạng thái\", status);\n        AddRow(table, \"File đang chạy\", location);\n        AddRow(table, \"Phát triển & duy trì\", new Label\n        {\n            Text = \"tamnv2 — Chuyên viên Pick Pack 1291\",\n            AutoSize = true,\n            Padding = new Padding(0, 8, 0, 8)\n        });\n        AddRow(table, \"Thao tác\", actions);\n""",
)

# 4) Strict numeric location form with backward-compatible normalization of legacy LTA/Shelving values.
old_location = r'''internal static partial class LocationNormalizer
{
    [GeneratedRegex(@"(?<![\d.])(\d{1,2})\s*\.\s*(\d{1,2})(?:\s*\.\s*(\d{1,2}))?(?!\s*\.)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PositionRegex();

    public static bool TryNormalize(string? raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            error = "Chưa nhập vị trí phát hiện hư hỏng.";
            return false;
        }

        var matches = PositionRegex().Matches(text);
        if (matches.Count != 1)
        {
            error = matches.Count == 0
                ? "Vị trí phải có dạng xx.yy hoặc xx.yy.zz. Ví dụ: 10.1, LTA 10.01, Shelving 12.2.1."
                : "Phát hiện nhiều hơn một vị trí trong nội dung nhập. Hãy chỉ nhập một vị trí.";
            return false;
        }

        var m = matches[0];
        if (!int.TryParse(m.Groups[1].Value, out var a) ||
            !int.TryParse(m.Groups[2].Value, out var b) ||
            a is < 0 or > 99 || b is < 0 or > 99)
        {
            error = "Vị trí không hợp lệ.";
            return false;
        }

        if (!m.Groups[3].Success)
        {
            normalized = $"LTA {a:00}.{b:00}";
            return true;
        }

        if (!int.TryParse(m.Groups[3].Value, out var c) || c is < 0 or > 99)
        {
            error = "Vị trí Shelving không hợp lệ.";
            return false;
        }

        normalized = $"Shelving {a:00}.{b:00}.{c:00}";
        return true;
    }
}
'''
new_location = r'''internal static partial class LocationNormalizer
{
    [GeneratedRegex(@"^(\d{1,2})\s*\.\s*(\d{1,2})(?:\s*\.\s*(\d{1,2}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex PositionRegex();

    public static bool TryNormalize(string? raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            error = "Chưa nhập vị trí phát hiện hư hỏng.";
            return false;
        }

        // Backward compatibility only for records created by earlier versions.
        if (text.StartsWith("LTA ", StringComparison.OrdinalIgnoreCase)) text = text[4..].Trim();
        else if (text.StartsWith("Shelving ", StringComparison.OrdinalIgnoreCase)) text = text[9..].Trim();

        var match = PositionRegex().Match(text);
        if (!match.Success)
        {
            error = "Vị trí chỉ được nhập số và dấu chấm, đúng dạng xx.yy hoặc xx.yy.zz. Ví dụ: 1.2 → 01.02; 1.2.3 → 01.02.03.";
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var a) ||
            !int.TryParse(match.Groups[2].Value, out var b) ||
            a is < 0 or > 99 || b is < 0 or > 99)
        {
            error = "Vị trí không hợp lệ.";
            return false;
        }

        if (!match.Groups[3].Success)
        {
            normalized = $"{a:00}.{b:00}";
            return true;
        }

        if (!int.TryParse(match.Groups[3].Value, out var c) || c is < 0 or > 99)
        {
            error = "Vị trí không hợp lệ.";
            return false;
        }

        normalized = $"{a:00}.{b:00}.{c:00}";
        return true;
    }
}
'''
replace_once("src/PickfaceDamage1291/EntryUiHelpers.cs", old_location, new_location)

# 5) Editing an existing report follows the same SKU/location input rules and button theme.
replace_once(
    "src/PickfaceDamage1291/DamageReportEditDialog.cs",
    """        _sku.CharacterCasing = CharacterCasing.Upper;\n        _sku.Leave += (_, _) => LookupSku();\n""",
    """        _sku.CharacterCasing = CharacterCasing.Upper;\n        EntryInputRules.AttachDigitsOnly(_sku);\n        EntryInputRules.AttachLocation(_location);\n        _sku.Leave += (_, _) => LookupSku();\n""",
)
replace_once(
    "src/PickfaceDamage1291/DamageReportEditDialog.cs",
    """        scroll.Controls.Add(root);\n        Controls.Add(scroll);\n\n        LoadValues();\n""",
    """        scroll.Controls.Add(root);\n        Controls.Add(scroll);\n        AppUiStyle.StyleAllButtons(this);\n\n        LoadValues();\n""",
)

# 6) Apps Script: authenticated self-service email change updates Auth, RTDB profile and username alias together.
replace_once(
    "apps-script/GoogleGateway/Code.gs",
    """    if (action === 'sync_login_aliases') {\n      requireAdmin_(auth.profile);\n      const count = syncLoginAliases_(idToken);\n      return json_({ ok: true, count: count });\n    }\n""",
    """    if (action === 'change_own_email') {\n      return json_(changeOwnEmail_(auth, String(payload.current_password || ''), String(payload.new_email || '')));\n    }\n\n    if (action === 'sync_login_aliases') {\n      requireAdmin_(auth.profile);\n      const count = syncLoginAliases_(idToken);\n      return json_({ ok: true, count: count });\n    }\n""",
)
replace_once(
    "apps-script/GoogleGateway/Code.gs",
    """  return { uid: user.localId, profile: profile };\n}\n""",
    """  return { uid: user.localId, profile: profile, firebase_email: String(user.email || profile.email || '') };\n}\n""",
)
insert_after = """function passwordResetByUsername_(username) {\n  const normalized = normalizeUsername_(username);\n  if (!normalized) return { ok: true };\n  const email = PropertiesService.getScriptProperties().getProperty(aliasKey_(normalized));\n  if (!email) return { ok: true };\n\n  const response = UrlFetchApp.fetch(\n    'https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),\n    {\n      method: 'post',\n      contentType: 'application/json',\n      payload: JSON.stringify({ requestType: 'PASSWORD_RESET', email: email }),\n      muteHttpExceptions: true\n    }\n  );\n  if (response.getResponseCode() !== 200)\n    throw new Error('Không gửi được yêu cầu đặt lại mật khẩu. Hãy thử lại sau.');\n  return { ok: true };\n}\n"""
change_email_function = r'''

function changeOwnEmail_(auth, currentPassword, newEmail) {
  const oldEmail = String(auth.firebase_email || auth.profile.email || '').trim();
  const username = normalizeUsername_(auth.profile.username || '');
  newEmail = String(newEmail || '').trim().toLowerCase();
  if (!oldEmail || !username) throw new Error('Không xác định được thông tin tài khoản hiện tại.');
  if (!currentPassword) throw new Error('Chưa nhập mật khẩu hiện tại.');
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(newEmail)) throw new Error('Email mới không hợp lệ.');
  if (oldEmail.toLowerCase() === newEmail.toLowerCase()) throw new Error('Email mới đang trùng email hiện tại.');

  const signIn = UrlFetchApp.fetch(
    'https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
    {
      method: 'post',
      contentType: 'application/json',
      payload: JSON.stringify({ email: oldEmail, password: currentPassword, returnSecureToken: true }),
      muteHttpExceptions: true
    }
  );
  if (signIn.getResponseCode() !== 200) throw new Error('Mật khẩu hiện tại không đúng.');
  const signed = JSON.parse(signIn.getContentText() || '{}');
  if (String(signed.localId || '') !== String(auth.uid || '')) throw new Error('Không xác minh được tài khoản hiện tại.');

  const update = UrlFetchApp.fetch(
    'https://identitytoolkit.googleapis.com/v1/accounts:update?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
    {
      method: 'post',
      contentType: 'application/json',
      payload: JSON.stringify({ idToken: String(signed.idToken || ''), email: newEmail, returnSecureToken: true }),
      muteHttpExceptions: true
    }
  );
  if (update.getResponseCode() !== 200) {
    const body = JSON.parse(update.getContentText() || '{}');
    const code = body && body.error ? String(body.error.message || '') : '';
    if (code === 'EMAIL_EXISTS') throw new Error('Email mới đã được sử dụng bởi tài khoản khác.');
    throw new Error('Firebase không chấp nhận email mới.');
  }

  const updated = JSON.parse(update.getContentText() || '{}');
  const newIdToken = String(updated.idToken || '');
  if (!newIdToken) throw new Error('Firebase không trả phiên mới sau khi đổi email.');

  const oldProfile = JSON.parse(JSON.stringify(auth.profile || {}));
  const newProfile = JSON.parse(JSON.stringify(oldProfile));
  newProfile.email = newEmail;

  try {
    const profileWrite = UrlFetchApp.fetch(
      CFG.FIREBASE_DB_URL + '/users/' + encodeURIComponent(auth.uid) + '.json?auth=' + encodeURIComponent(newIdToken),
      {
        method: 'put',
        contentType: 'application/json',
        payload: JSON.stringify(newProfile),
        muteHttpExceptions: true
      }
    );
    if (profileWrite.getResponseCode() !== 200) throw new Error('Không cập nhật được hồ sơ tài khoản.');

    PropertiesService.getScriptProperties().setProperty(aliasKey_(username), newEmail);
    return {
      ok: true,
      local_id: String(updated.localId || auth.uid || ''),
      email: newEmail,
      id_token: newIdToken,
      refresh_token: String(updated.refreshToken || ''),
      expires_in: String(updated.expiresIn || '3600')
    };
  } catch (err) {
    // Fail closed: try to put Authentication + RTDB + alias back to the old email.
    try {
      const rollback = UrlFetchApp.fetch(
        'https://identitytoolkit.googleapis.com/v1/accounts:update?key=' + encodeURIComponent(CFG.FIREBASE_API_KEY),
        {
          method: 'post',
          contentType: 'application/json',
          payload: JSON.stringify({ idToken: newIdToken, email: oldEmail, returnSecureToken: true }),
          muteHttpExceptions: true
        }
      );
      const rollbackBody = JSON.parse(rollback.getContentText() || '{}');
      const rollbackToken = String(rollbackBody.idToken || '');
      if (rollback.getResponseCode() === 200 && rollbackToken) {
        UrlFetchApp.fetch(
          CFG.FIREBASE_DB_URL + '/users/' + encodeURIComponent(auth.uid) + '.json?auth=' + encodeURIComponent(rollbackToken),
          {
            method: 'put',
            contentType: 'application/json',
            payload: JSON.stringify(oldProfile),
            muteHttpExceptions: true
          }
        );
      }
      PropertiesService.getScriptProperties().setProperty(aliasKey_(username), oldEmail);
    } catch (_) {}
    throw new Error('Không thể hoàn tất đổi email. Hệ thống đã cố gắng khôi phục email cũ. ' + cleanError_(err));
  }
}
'''
text = read("apps-script/GoogleGateway/Code.gs")
if insert_after not in text:
    raise SystemExit("passwordResetByUsername_ block not found for changeOwnEmail_ insertion")
write("apps-script/GoogleGateway/Code.gs", text.replace(insert_after, insert_after + change_email_function, 1))

# 7) Version bump.
replace_once(
    "src/PickfaceDamage1291/PickfaceDamage1291.csproj",
    """    <Version>1.3.2</Version>\n    <AssemblyVersion>1.3.2.0</AssemblyVersion>\n    <FileVersion>1.3.2.0</FileVersion>\n""",
    """    <Version>1.3.3</Version>\n    <AssemblyVersion>1.3.3.0</AssemblyVersion>\n    <FileVersion>1.3.3.0</FileVersion>\n""",
)

print("v1.3.3 patch applied")
