# v1.4.2 OWNER test checklist

1. **Gửi thông tin hư hỏng**: để thiếu SKU/vị trí/ca/số lượng hoặc SKU không hợp lệ -> nút phải khóa. Nhập đủ hợp lệ và phiên được phép -> nút mở.
2. **Tiến trình góc phải dưới**: chạy đồng bộ, export Excel, import SKU hoặc đổi mạng -> phải thấy tiến trình. Notification góc trái dưới vẫn hoạt động độc lập.
3. **Excel**: xuất ít nhất 2 SKU có ảnh khác nhau; kiểm tra từng ảnh nằm đúng dòng SKU. Với ảnh chưa cache local, app phải tải ảnh Drive đúng `file_id` trước khi nhúng.
4. **Đổi mạng**: mở app khi có Internet, chuyển sang mạng nội bộ, sau đó chuyển lại Internet; app không được treo. Phải có tiến trình khôi phục và tự sync lại khi endpoint truy cập được.
5. **Tốc độ**: đăng nhập không còn chờ flush toàn bộ audit cũ trước khi mở màn hình chính. SKU sync dùng batch tối đa 1.000/request.
6. **Regression**: kiểm tra login, notification, danh sách phiếu, chỉnh sửa/xóa, lịch sử và cập nhật phiên bản vẫn hoạt động.
