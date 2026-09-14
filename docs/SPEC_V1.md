# Đặc tả V1 — Cập nhật hư hỏng Pickface 1291

## 1. Môi trường

- Windows 10/11 x64.
- Người dùng chỉ có quyền user, không cần quyền Administrator.
- UI native WinForms, không chạy local web server, không mở port LAN.
- Dữ liệu ứng dụng nằm trong `%LOCALAPPDATA%\PickfaceDamage1291`.

## 2. Danh mục SKU

Nguồn chuẩn import là file Excel dạng `REPORT_BIN_INVENTORY...xlsx`.

Ba header bắt buộc:

- `SKU`
- `Tên sản phẩm`
- `Base Units`

Quy tắc:

- `SKU` là khóa chính duy nhất và luôn được lưu dạng TEXT.
- Cùng SKU + cùng Tên + cùng Base: bỏ qua duplicate trong file.
- SKU mới: thêm database.
- SKU đã có nhưng Tên/Base thay đổi: Owner quyết định giữ cũ hay dùng mới.
- SKU khác nhưng cùng Tên hoặc Base: hợp lệ, không coi là xung đột.
- SKU cũ không xuất hiện trong inventory mới: không tự xóa.
- Lưu `first_seen_at`, `last_seen_at`, file nguồn và lịch sử quyết định thay đổi.
- Cùng file đã import (SHA-256 giống): chặn import lại.

## 3. Nhập hư hỏng

Trường dữ liệu:

- SKU.
- Tên sản phẩm tự điền từ SQLite, read-only.
- Vị trí phát hiện.
- Ngày phát hiện, mặc định hôm nay nhưng được sửa.
- Giờ và phút tách riêng.
- Ca 1/Ca 2.
- Số lượng hư hỏng > 0, hỗ trợ số thập phân.
- Base Units tự điền từ SQLite, read-only.
- Tối đa 5 ảnh.

Ảnh được copy vào local staging ngay khi chọn để không phụ thuộc file gốc.

## 4. Local-first

Khi bấm Gửi:

1. Validate dữ liệu.
2. Tạo `report_id` UUID.
3. Lưu SQLite trước.
4. Ảnh đã được giữ local.
5. Nếu Google sẵn sàng thì đồng bộ.
6. Nếu mất mạng/lỗi Google thì giữ trạng thái PENDING/ERROR và cho phép retry.

Trạng thái local: `PENDING`, `SYNCING`, `SYNCED`, `ERROR`.

## 5. Google

Chỉ dùng Google OAuth Desktop và scope `https://www.googleapis.com/auth/drive.file`.

Ứng dụng tự tạo/kiểm tra:

- Folder mẹ `CẬP NHẬT HƯ HỎNG PICKFACE`.
- Folder con `Ảnh hàng hư hỏng`.
- Spreadsheet `Cập nhật thông tin hư hỏng pickface 1291`.

Sheet có 16 cột:

`ID | Ngày phát hiện | Giờ phát hiện | Ca | SKU | Tên sản phẩm | Vị trí phát hiện | Số lượng hư hỏng | Base Units | Ảnh 1 | Ảnh 2 | Ảnh 3 | Ảnh 4 | Ảnh 5 | Thời gian nhập | Thời gian đồng bộ`

Tên ảnh Drive dùng định dạng an toàn Windows/Drive:

`yyyy-MM-dd_HH-mm_Ca-x_SKU_TênSP_VịTrí_SL-x_01.jpg`

Retry không upload lại ảnh đã có `drive_file_id`. Trước khi append Sheet, ứng dụng kiểm tra `report_id` để chống ghi trùng.

## 6. GitHub

Repository public: `tamnv2/pickfacedamage1291`.

Không được commit:

- OAuth Client JSON.
- Google token.
- SQLite thực tế.
- ảnh thực tế.
- file dữ liệu nghiệp vụ.

GitHub Actions tự build Windows x64 self-contained. Tag `v*` sẽ tạo GitHub Release ZIP.

## 7. Chưa làm trong V1

- Logic xuất Excel nghiệp vụ (chỉ giữ nút placeholder).
- LAN/web UI cho máy khác.
- Multi-user/server online.
