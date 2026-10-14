# OWNER decisions — PICKFACE DAMAGE 1291

Authority: explicit OWNER commands and evidence stored in this repository. This document is not a raw chat transcript. New approved changes must also appear in `ops/change-continuity/PFD-001.json`.

## 2026-10-14 — PFD-001 (approved)

1. **A — repository-first project continuity.** OWNER approved consulting `tamnv2/supra-inventory` **read-only for the persistence mechanism only**; implement durable sanitized working discussion, decisions, pending items, implementation, release and test states in `tamnv2/pickfacedamage1291`. Never use AI memory as the authoritative project record. A must be verified complete before B.
2. **B — migrate all Apps Script Gateway/UrlFetchApp flows.** Target project `pickface-damage-1291`, Cloud Run `asia-southeast1`, request-based, scale-to-zero; preserve Firebase Auth/RTDB, Sheets/Drive records, image links and conflicts, username identity and audit, RBAC, operator-lease correctness. No new Cloudflare resources; Office network can be unsupported. No cross-project resource reuse. Do not delete legacy Apps Script, Drive files or grant permissions without a separate explicit OWNER request.
3. **C — Cloud Run usage instead of Apps Script usage.** Replace old in-app table; show Cloud Run request, CPU and memory, cost/free-tier projection when verified provider data available, distinguish project-only metrics from billing-account-shared allowance. Warn before free-tier exhaustion, with no fake 0%/guaranteed-free claim.
4. **Consumption evidence before migration.** 25 days of client usage snapshots yielded ~35,978 Apps Script gateway requests, of which ~29,206 were RTDB proxy requests. Approximate ~4,000 Apps Script UrlFetchApp calls/day from code-path estimates, not Google actual quota.
5. **Owner authorization scope.** The previous approval to build and deploy B/C is acknowledged; a provider permission change, destructive migration or removal still requires distinct explicit approval. Mark CODE_READY, CI_PASS, DEPLOYED, FIELD_PASS separately.

## Historical release baseline

`docs/handovers/HANDOVER_CURRENT.md` documents Windows v1.4.33, GitHub-only update binary and previous Apps Script runtime. This is evidence of a prior release, **not** proof all deployed PCs run that version.

## Unresolved questions

- Which project-scoped Cloud Run deployment credentials / GitHub Actions identity are available?
- Cloud Run Google Drive/Sheets authority for current fixed root (My Drive vs Shared Drive); any OAuth/grant must be OWNER-approved and server-side only.
- How to preserve Apps Script `PropertiesService` username aliases and change sequence safely without moving files out of scope?
- How to measure CPU/RAM and shared Billing Account allowance without inspecting any other GCP project?

## 2026-10-14 — Chạy song song Gateway cũ và Cloud Run (OWNER chốt)

- Máy cài bản Windows cũ tiếp tục sử dụng Apps Script Gateway, tài khoản và dữ liệu hiện tại khi quota còn khả dụng; không tự thay URL Gateway hoặc phát hành bản stable bắt buộc nâng cấp.
- Bản mới sẽ kiểm thử qua Cloud Run song song. OWNER chấp nhận khả năng tạo phiếu trùng, nhưng không chấp nhận mất dữ liệu, ghi đè sai, phá hủy tài nguyên hay vô tình chặn máy cũ.
- Chỉ vô hiệu hóa nghiệp vụ của Gateway cũ sau khi Cloud Run đã kiểm thử đạt yêu cầu **và có lệnh mới, rõ ràng của OWNER**. Không xóa dữ liệu, dự án hay ứng dụng cũ trong đợt thử nghiệm.
- Trước khi thử ghi, cần thống nhất phương án tránh xung đột khi hai hệ thống dùng chung Google Sheets (Apps Script LockService không khóa Cloud Run), bộ đếm change_seq và Firebase active_operator. Đề xuất tách vùng ghi thử nghiệm và trạng thái lease cho bản mới; thiết kế chi tiết còn chờ duyệt.
- Trong giai đoạn chạy song song, dự án vẫn tiêu thụ UrlFetchApp từ máy cũ; không được báo đã về 0 trước khi ngừng thực tế.

## 2026-10-14 — PFD-001: Quyết định lưu trữ thời gian thử nghiệm (OWNER APPROVED)

- Phiên bản Cloud Run **được phép đọc lịch sử V1** từ Google Sheets/Drive hiện hữu, nhưng không được ghi sửa/xoá các bản ghi, ảnh hay log V1 trong thời gian chạy song song.
- Cloud Run V2 **ghi riêng** dữ liệu phiếu, danh mục thay đổi, ảnh, nhật ký và bộ đếm/lease của V2 trong những tài nguyên con thực sự thuộc Drive root đã chỉ định hoặc project GCP hợp lệ. Máy V1 không nhìn thấy dữ liệu V2.
- Giữ Firebase identity hiện có để kiểm tra đăng nhập nhưng không sửa, khóa, đổi email hay quyền tài khoản cũ trong thời gian thử nghiệm.
- OWNER yêu cầu rà soát toàn bộ thiết lập Google Cloud Run và điểm cần OWNER cấp quyền. Sau khi cấp quyền đầy đủ mới tiếp tục triển khai logic phần mềm mới; phải kiểm tra và trình OWNER chốt trước khi chạy triển khai mô hình mới.
- Các thao tác **cấp quyền IAM, cấp quyền Drive/OAuth, liên kết Billing, cho Cloud Run endpoint công khai** vẫn là hành động nhạy cảm cần OWNER xác nhận cụ thể, không được tự suy diễn thành đã duyệt.
- Không làm ảnh hưởng Apps Script Gateway, `runtime-config.json` và GitHub stable release cũ; chỉ có OWNER mới ra lệnh khai tử mô hình cũ sau kiểm thử.
- Drive metadata đã xác minh root là **My Drive, không phải Shared Drive**. V2 cần danh tính có thể tạo ảnh/tệp trong My Drive theo uỷ quyền người dùng hoặc một giải pháp lưu trữ khác được OWNER duyệt; việc chia sẻ service account editor đơn thuần không đủ bảo đảm tạo file.

Chi tiết checklist: `docs/plans/PFD-001_CLOUD_RUN_BOOTSTRAP.md`. Đây là đặc tả thủ tục chờ quyền, **không phải xác nhận Cloud Run đã được cấu hình**.
