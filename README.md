# Pickface Damage 1291

Ứng dụng Windows portable để cập nhật hàng hư hỏng Pickface 1291.

## Kiến trúc V1

- C# / .NET 8 WinForms, chạy bằng quyền user thường.
- SQLite lưu danh mục SKU, phiếu hư hỏng, lịch sử import và hàng đợi đồng bộ.
- Import Excel theo header `SKU`, `Tên sản phẩm`, `Base Units`; SKU là khóa chính.
- Google OAuth Desktop + Drive API + Sheets API, không dùng service trung gian.
- Local-first: luôn lưu trên laptop trước, sau đó mới đồng bộ Google.
- Ảnh tối đa 5 ảnh/phiếu, copy vào local staging ngay khi chọn.
- GitHub Actions build bản `win-x64` self-contained.

## Bảo mật

Repository là public. Tuyệt đối không commit OAuth client JSON, token Google, database local hoặc dữ liệu thực tế. Các file này đã được chặn bằng `.gitignore`.

## Trạng thái

Đang khởi tạo V1 theo đặc tả Owner ngày 14/09/2026.
