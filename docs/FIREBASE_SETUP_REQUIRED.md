# Firebase setup bắt buộc — project `pickface-damage-1291`

Phạm vi: chỉ Google Cloud project `pickface-damage-1291`. Không tạo project mới khác.

## 1. Add Firebase vào Google Cloud project hiện có

1. Mở Firebase Console.
2. Chọn tạo/add project.
3. Chọn **Add Firebase to Google Cloud project**.
4. Chọn đúng project `pickface-damage-1291`.
5. Giữ **Spark / no-cost**.
6. Không gắn billing account.
7. Google Analytics không cần cho ứng dụng này, có thể bỏ qua.

## 2. Authentication

Firebase Console > Authentication > Get started > Sign-in method:

- Bật **Email/Password**.
- Không bật Phone/SMS.
- Không cần Google/Social login.

Password reset sẽ dùng email đăng ký.

## 3. Realtime Database

Firebase Console > Realtime Database > Create database:

- Region: **Singapore (`asia-southeast1`)** nếu được hiển thị.
- Không chọn test mode lâu dài.
- Sau khi tạo, copy chính xác **Database URL**.

Ví dụ định dạng:

`https://<database-name>.asia-southeast1.firebasedatabase.app`

## 4. Public client config cần OWNER gửi lại

Không gửi password, client secret, service-account JSON hoặc private key.

Chỉ cần gửi 3 giá trị không phải secret:

1. **Firebase Web API Key** của project `pickface-damage-1291`.
2. **Realtime Database URL**.
3. **Google OAuth Desktop Client ID** đã tạo trước đó, dạng `....apps.googleusercontent.com`.

Các giá trị trên sẽ được đưa vào build để ứng dụng không cần chọn file JSON trên từng laptop.

## 5. Security Rules

Sau khi OWNER gửi API key + Database URL, source sẽ có rules chính thức để OWNER paste/deploy vào Realtime Database. Quy tắc mục tiêu:

- mặc định deny;
- chỉ account đã Authentication + profile `active=true` mới được dùng;
- USER chỉ đọc profile của mình và dữ liệu trạng thái cần thiết;
- ADMIN quản lý profile/permission;
- USER không tự đổi role/permission;
- chỉ role USER được ghi `active_operator`;
- takeover khi phiên cũ online hoặc lease đã hết;
- nếu phiên cũ mất mạng nhưng lease 30 phút còn hiệu lực thì không takeover;
- audit chỉ append, không sửa bản ghi cũ;
- không lưu password/token trong Realtime Database.

## 6. Target free bắt buộc

- Firebase Spark / no-cost.
- Không Cloud Functions.
- Không Cloud Run.
- Không Firestore.
- Không Firebase Storage.
- Không Phone/SMS Auth.
- Heartbeat active USER: 60 giây.
- Offline lease: 30 phút.
- Chỉ listener realtime vào các node nhỏ cần thiết.
- Không polling liên tục.

Nếu dịch vụ chạm giới hạn free: ưu tiên hạn chế/dừng chức năng, tuyệt đối không tự nâng Blaze hoặc bật billing.
