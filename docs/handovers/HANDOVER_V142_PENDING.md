# PICKFACE DAMAGE 1291 — v1.4.2 pending verification

Branch: `fix/v1.4.2-progress-export-network`
Base: `main` at `1ca398df68252def122cdde4b2ba9f5a1ae791b3`

## OWNER requirements in this batch

1. Nút **Gửi thông tin hư hỏng** chỉ được mở khi các trường bắt buộc hợp lệ và quyền phiên cho phép.
2. Hiển thị tiến trình công việc đang chạy ở góc dưới bên phải; notification góc dưới bên trái giữ nguyên.
3. Sửa export Excel, ảnh phải thuộc đúng `report_id`/SKU và tải từ Drive theo metadata đã pull từ Google Sheet.
4. Tối ưu tốc độ app desktop <> Google nhưng không đổi business truth.
5. Khi đổi Wi‑Fi/LAN, app phải tự phục hồi kết nối thay vì bám DNS/TCP/proxy cũ.

## Implemented

- `V142Runtime.cs`: task progress surface, network-change recovery, initial required-field guard.
- `V142RuntimeCorrections.cs`: fail-closed correction using the real entry status label; validates SKU lookup result, location, shift and quantity before enabling Send.
- `DamageReportExportService.cs`: image resolution scoped by `report_id`, sequence ordering, Drive download progress, detailed mapping logs, ClosedXML placement/resize fix.
- `CloudSyncService.cs`: SKU/report pages raised to gateway maximum 1,000 rows/request and explicit audit-sync progress.
- `SessionBootstrap.cs`: audit backlog no longer blocks a valid login; LOGIN_SUCCESS is queued durably and transport continues asynchronously.
- `NetworkHttpClientFactory.cs`: Windows network-change events, short pooled-connection lifetime, dynamic system proxy re-resolution and recovery trigger.
- `Program.cs`: activates v1.4.2 runtime layers.
- project version bumped to `1.4.2`.

## Safety / scope

- Repo only: `tamnv2/pickfacedamage1291`.
- No Drive writes in this change session.
- No Google Cloud changes/deployments in this change session.
- No Apps Script change is required for these client changes.
- No credentials/secrets added.

## Verification required before merge/release

- GitHub Windows CI must publish successfully.
- Test Send button: blank/invalid fields => disabled; valid complete fields + allowed operator state => enabled.
- Test Excel with multiple SKUs and multiple images, including rows whose images are not cached locally.
- Test switching Internet -> internal LAN -> Internet while app stays open; verify progress UI and automatic resync.
- Confirm notifications remain bottom-left and task progress remains bottom-right.
