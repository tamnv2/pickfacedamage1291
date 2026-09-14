# Bootstrap ADMIN đầu tiên — PICKFACE DAMAGE 1291

Chỉ áp dụng cho Firebase/Google Cloud project `pickface-damage-1291`.

## 1. Authentication

Firebase Console > Authentication > Users > Add user.

Tạo tài khoản ADMIN đầu tiên bằng email thật của OWNER và mật khẩu OWNER tự đặt. Không đưa mật khẩu vào GitHub hoặc gửi cho AI.

Sau khi tạo, copy `User UID`.

## 2. Realtime Database profile

Firebase Console > Realtime Database > Data.

Tạo node:

`users/<USER_UID>`

với các trường:

- `uid`: đúng USER_UID ở Authentication.
- `username`: tên tài khoản OWNER muốn dùng.
- `display_name`: tên hiển thị.
- `email`: đúng email Authentication.
- `role`: `admin`.
- `active`: `true`.
- `permissions/all`: `true`.

ADMIN đầu tiên phải được bootstrap thủ công một lần vì Security Rules mặc định không cho client tự nâng quyền admin.

## 3. Security Rules

Realtime Database > Rules: dùng file `firebase/database.rules.json` trong chính repository này làm nguồn chuẩn. Không dùng Test mode.

Sau khi Publish rules, đăng nhập app bằng ADMIN đầu tiên để kiểm tra Authentication + profile trước khi tạo USER khác.

## 4. Không làm

- Không tạo service-account key cho desktop app.
- Không lưu password/token vào Realtime Database.
- Không bật Blaze/billing.
- Không bật Phone/SMS Auth.
- Không dùng project Firebase/Google Cloud khác.
