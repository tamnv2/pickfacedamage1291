# Đặc tả V2 — Multi-user và offline

Tuân thủ `PROJECT_SCOPE_LOCK.md`.

## Kiến trúc
- WinForms native, self-contained, không cần Administrator.
- SQLite local làm cache, draft và hàng đợi đồng bộ.
- Firebase Authentication cho email/password và lấy lại mật khẩu qua email.
- Firebase Realtime Database cho profile, role, permission, active_operator, presence và audit nhỏ gọn.
- Google Drive + Google Sheet là dữ liệu nghiệp vụ sau cùng.
- Không Cloud Run, Cloud Functions, Firestore hay Firebase Storage ở giai đoạn này.
- Giữ Firebase ở Spark/no-cost; không gắn billing khi chưa có lệnh OWNER.

## Role
ADMIN: quản trị toàn bộ, không tham gia khóa active_operator, không kick USER và không bị USER kick. Khi USER đang giữ quyền nhập, ADMIN không tạo phiếu hư hỏng mới.

USER: chỉ dùng quyền được cấp. Tại một thời điểm chỉ một USER được giữ quyền nhập hư hỏng trên toàn hệ thống.

## Offline lease
`/active_operator` lưu uid, username, session_id, device_id, generation, acquired_at, lease_until, last_seen.

- Heartbeat mặc định 60 giây.
- Lease offline mặc định 30 phút từ lần xác nhận server cuối.
- USER mới không takeover một phiên offline khi lease cũ còn hiệu lực.
- Hết lease khi vẫn offline: giữ draft/local nhưng không xác nhận phiếu mới.
- Online trở lại phải kiểm tra session/generation trước khi tiếp tục.
- USER đã từng xác thực online trên laptop đó có thể mở offline; laptop chưa từng xác thực account đó không được login offline lần đầu.

## Drive cố định
Ứng dụng không tìm hoặc tạo folder mẹ ở nơi khác.

- Root: `16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC`
- Ảnh: `1K_lUl_uE4dskR28cVK4iZJrzFUINfvXf`
- Sheet: `1Ubm9EhALocUovzVr3UIspdCMtHIPm2NPUw6UjlZcqw4`

Không truy cập được các ID này thì báo lỗi và dừng phần đồng bộ Google.

## Nhập hư hỏng
- SKU lưu TEXT.
- Enter/rời ô SKU lookup ngay.
- SKU sai: Tên sản phẩm hiển thị `Sản phẩm không có trong cơ sở dữ liệu, hãy kiểm tra lại.` và khóa Gửi.
- Ngày chọn bằng calendar dd/MM/yyyy.
- HH/mm dùng spinner số.
- Ca 1/Ca 2 và nhớ lựa chọn gần nhất theo user.
- Số lượng là số nguyên > 0.
- Tối đa 5 ảnh.

## Audit
Chỉ ADMIN có tab Lịch sử. Ghi user, thời gian, thiết bị, session, hành động, object, before/after, kết quả và lỗi/lý do nếu có. Không ghi mật khẩu hoặc token.

## UI
Tách rõ section, tăng padding và khoảng cách giữa label/input. Tab dự kiến: Nhập hư hỏng, Danh sách, Danh mục SKU, Quản lý nhân sự (ADMIN), Lịch sử (ADMIN), Cài đặt.

## Cập nhật phiên bản
Chỉ kiểm tra GitHub Release của `tamnv2/pickfacedamage1291`. Khi có bản mới: hiển thị version/changelog, tải asset Windows x64 về thư mục tạm, xác minh gói, thoát app, thay EXE trong thư mục user có quyền ghi rồi mở lại. Nếu không thể tự thay file thì giữ nguyên bản cũ và mở trang Release để cập nhật thủ công.

## Target no-cost
- Firebase Spark/no-cost, không billing.
- RTDB storage mục tiêu <= 50 MB.
- RTDB download mục tiêu <= 1 GB/tháng.
- Concurrent connections mục tiêu <= 20.
- Auth active users mục tiêu <= 50/ngày.
- Không polling liên tục; realtime listener chỉ theo dõi node nhỏ cần thiết.
