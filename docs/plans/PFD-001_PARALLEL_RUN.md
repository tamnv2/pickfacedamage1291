# PFD-001 — Chạy song song Apps Script Gateway và Cloud Run

**Trạng thái:** OWNER đã chốt nguyên tắc cùng tồn tại và chỉ ngừng Gateway cũ theo lệnh sau này; các chi tiết tách dữ liệu dưới đây là **đề xuất kỹ thuật đang chờ xác nhận**, chưa triển khai.

## Ranh giới vận hành

- Bản Windows cũ: dùng nguyên URL Google Apps Script hiện hữu, Google Sheets/Drive hiện tại, Firebase hiện tại; không đổi `runtime-config.json` khóa `google_gateway_url`, không sửa/quyền/khai tử deployment GAS.
- Bản Cloud Run thử nghiệm: URL độc lập, bản build phát cho các máy test có chủ đích; không phát hành stable GitHub `releases/latest` làm máy cũ tự nhận bản mới. `.github/workflows/release-windows.yml` tự publish stable sau thay đổi `src/**` trên `main`, nên phải giữ code/test trong Draft PR hoặc kênh artifact/prerelease có kiểm soát.
- Sau khi đạt field PASS và được OWNER ra **lệnh mới**: mới vô hiệu hóa đường nghiệp vụ Gateway cũ. Không xóa file Drive/Sheets/ảnh hoặc repo, không tự ý tắt Firebase của project.
- Chấp nhận bản cũ tiếp tục dùng UrlFetchApp trong giai đoạn song song; chỉ bản mới có mục tiêu không phát sinh UrlFetchApp.

## Tại sao không chỉ thay URL?

| Tài nguyên dùng chung | Dấu hiệu từ nguồn cũ | Rủi ro | Phương án thử nghiệm ưu tiên |
| --- | --- | --- | --- |
| Phiếu Google Sheets | `Code.gs` dùng `LockService` riêng GAS và `REPORT_CHANGE_SEQ_V140` | Hai backend không chung lock, có thể ghi đè sai/lặp seq, mất liên kết ảnh; không chỉ trùng phiếu | V2 ghi tab/bảng thử riêng; đọc legacy có thể read-only |
| Danh mục SKU | `PRODUCT_CHANGE_SEQ_V140`/PropertiesService | Lặp seq và ghi đè SKU | V2 đồng bộ trên không gian riêng |
| Ảnh/log Drive | Thư mục con cố định dưới root; hard-delete trong Apps Script | Xóa nhầm ảnh, orphan, lẫn log | Thư mục con dành cho test, phải xác minh thực thuộc root; tuyệt đối không hard-delete cross-model |
| Firebase `active_operator` | `OperatorLeaseManager`: global key, heartbeat 60s, lease 30 phút | Test bản mới có thể chiếm quyền, đá máy cũ | Dùng namespace/lease store độc lập và atomic; thay đổi rule/phân quyền phải có OWNER đồng ý |
| Tài khoản Firebase | `users/{uid}` + `PropertiesService` alias | Đổi email, mật khẩu/khóa user tác động cả hai | Khi test dùng user thử có kiểm soát; không ghi sửa user legacy |
| Runtime/Update | Windows chỉ chấp nhận `script.google.com`, updater stable GitHub | Cấu hình chung / bản release stable có thể tự chuyển bản cũ | Cloud Run endpoint gắn bản test mới; không thay chính sách stable |

## Phương án khuyến nghị để tránh xung đột nguy hiểm

1. **Legacy giữ nguyên** Google Sheets cũ, Apps Script deployment, Firebase auth/account/lease và image URLs.
2. **Cloud Run V2 test ghi tách biệt** trong các tab hoặc spreadsheet mới thực sự nằm trong Drive root được cấp phép; đặt tên nguồn/phiếu rõ ràng; riêng asset ảnh/log của V2 nằm dưới subtree đã xác minh.
3. V2 có thể **đọc** lịch sử cũ nhưng **không ghi sửa** các dòng production legacy trong thời gian test. Vì vậy máy cũ không mặc định nhìn thấy dữ liệu V2; có thể làm màn hình xem tổng hợp nếu OWNER cần.
4. Lease thao tác V2 độc lập; nếu cần mở quyền tạo key RTDB riêng hoặc quyền Drive mới phải xin OWNER phê duyệt rõ trước.
5. Dữ liệu V2 sau test chỉ nhập hợp nhất vào kho chuẩn sau khi kiểm kê, xuất biên bản đối soát và OWNER đồng ý. Trùng dữ liệu có thể giữ nguyên theo yêu cầu, nhưng không tự xóa để dedupe.
6. Tách chỉ số theo nguồn `legacy_apps_script` / `cloud_run_v2`. Cloud Run usage không chứng minh Apps Script usage của các máy cũ là 0.

## Kiểm tra bắt buộc trước khi bảo đảm chạy song song

- Legacy login, báo cáo, đồng bộ phiếu/ảnh, lịch sử và cập nhật tiếp tục PASS ngay cả khi V2 nhận request.
- V2 login, tạo phiếu test, upload nhiều ảnh, readback, sửa/xử lý trùng và retry PASS. Hai bản không đụng cùng row ID/seq write path.
- Thử hai máy cùng lúc: người đang nhập trên v1 không bị giành lease bởi v2; quyền ADMIN/USER hai phía không bị thay đổi bất ngờ.
- App cũ giữ version stable đang dùng; việc cài thử V2 chỉ theo thao tác chủ ý, không có auto update forced.
- Lập và thử quy trình rollback; giữ bản cũ trong suốt giai đoạn thử.
- Không đánh dấu B DONE trước khi backend ghi đầy đủ + ứng dụng mới + CI + triển khai + kiểm thử hiện trường; C vẫn chờ B.

## Giới hạn khi OWNER sau này ra lệnh ngừng mô hình cũ

Chặn Gateway cũ sẽ làm các nghiệp vụ cần Gateway không hoạt động, nhưng không thể cam kết tự gỡ hoặc vô hiệu hóa 100% chương trình Windows portable đang nằm offline trên máy. Cần phân biệt **chặn giao dịch server** với **khóa hoàn toàn executable trên thiết bị**; các thao tác Firebase trực tiếp của bản cũ cần được rà riêng để không tác động bản mới.
