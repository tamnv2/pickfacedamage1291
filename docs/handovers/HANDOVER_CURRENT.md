# HANDOVER CURRENT — PICKFACE DAMAGE 1291

Cập nhật: 24/09/2026

## 1. Phạm vi cố định

- GitHub duy nhất: `tamnv2/pickfacedamage1291`
- Google Drive root duy nhất: `16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC`
- Google Cloud project duy nhất: `pickface-damage-1291`
- Repo public: tuyệt đối không commit password, OAuth secret, refresh token, service-account key hoặc credential.

## 2. Trạng thái release hiện tại

- Release hiện hành: **v1.4.32**
- Main commit chứa thay đổi v1.4.32: `c8cfc8365547fab412f40d44e8317755b106b740`
- Asset: `PickfaceDamage1291-win-x64-v1.4.32.zip`
- SHA-256 GitHub asset: `3f31a790bc003a9046c34693503b69b08d5ef554f43174c7bdd01dadc1cf33c2`
- Build Windows Portable: PASS
- Publish Windows Release: PASS
- Deploy Google Apps Script: PASS
- Live Google Gateway đã PASS kiểm tra capability `release_metadata` và chính sách `download_transport=github_only`, `drive_mirror=false`.

## 3. Chính sách cập nhật đã chốt

### Mạng thường / truy cập GitHub được

1. Ứng dụng đọc GitHub Release.
2. Nếu có bản mới, hiển thị phiên bản + release notes.
3. Người dùng xác nhận.
4. File cập nhật **chỉ tải trực tiếp từ GitHub**.
5. Kiểm tra digest nếu GitHub cung cấp.
6. Stage EXE, đóng app, thay bản và mở lại.

### Mạng Office / GitHub bị chặn

1. Ứng dụng vẫn có thể phát hiện bản mới bằng **Google Gateway metadata-only**.
2. Google Gateway chỉ gọi GitHub API để trả metadata nhẹ: tag, release notes, asset name/url, digest/size.
3. **Không upload, lưu, cache hoặc truyền file ZIP cập nhật qua Google Drive.**
4. UI hiển thị có bản mới nhưng không cho tải qua đường Google.
5. Cảnh báo người dùng chuyển sang mạng có Internet và truy cập được GitHub, sau đó kiểm tra/cập nhật lại.
6. Khi đổi mạng, bấm cập nhật lại sẽ kiểm tra GitHub trực tiếp rồi mới cho tải.

## 4. Google Drive release mirror

Đã vô hiệu hóa hoàn toàn đường tạo/tải release mirror mới:

- Đã xóa code `ReleaseMirrorV1411.gs`.
- Đã xóa deployment marker mirror.
- Đã xóa các workflow warm Google Drive release mirror.
- `release-windows.yml` không còn job warm mirror.
- Client không còn `release_mirror_chunk` hoặc `DownloadFromGoogleMirrorAsync`.
- Regression guard chặn cơ chế mirror quay lại.

**Không xóa dữ liệu mirror cũ trên Drive** vì OWNER chưa yêu cầu xóa dữ liệu phá hủy. Kiểm tra sau khi phát hành v1.4.32 xác nhận thư mục ReleaseMirror không phát sinh ZIP/manifest v1.4.32; file mới nhất còn lại là v1.4.31.

## 5. Các thay đổi UI/chức năng ngay trước v1.4.32

Các thay đổi từ v1.4.31 đã nằm trong v1.4.32:

- Header: nâng Google status + version lên cùng hàng tiêu đề, tránh tụt/che.
- Lịch sử: tăng vùng toolbar và tách status để button không bị che ở DPI scale.
- Progress: hiển thị % rõ hơn cho đồng bộ, xuất Excel, tải/stage cập nhật.
- Xung đột đồng bộ:
  - Có nút **Xử lý xung đột**.
  - So sánh bản local và bản Google.
  - Không tự chọn bản thắng.
  - Chỉ ADMIN hoặc người tạo phiếu được quyết định.
  - Có lựa chọn dùng bản Google hoặc giữ bản local rồi ghi lên Google, đều cần xác nhận.
- Cài đặt:
  - Hiển thị dung lượng local phát sinh theo nhóm thực tế.
  - CSDL local được ghi rõ gồm phiếu + danh mục SKU + trạng thái sync chung trong SQLite, không bịa tách dung lượng SKU.
  - Hiển thị ảnh, logs, backup, dữ liệu khác.
  - Hiển thị RAM Working Set hiện tại.
  - Mọi user có thể xem.

## 6. Log đã kiểm tra trong phiên trước khi sửa

Log Drive gần nhất cho thấy app khởi động/login bình thường, Google OAuth kết nối thành công, Apps Script route sẵn sàng và log auto-upload hoạt động. Vấn đề trong yêu cầu hiện tại chủ yếu là UI/updater policy, không phải lỗi backend nghiêm trọng.

## 7. Regression / invariant quan trọng

Không được làm mất các invariant sau:

- Update binary chỉ từ GitHub.
- Office chỉ dùng Google Gateway để đọc metadata phiên bản.
- Không tạo release ZIP/manifest mới trên Drive.
- Không tự ghi đè khi sync conflict.
- Không xóa mirror Drive cũ nếu OWNER chưa yêu cầu rõ.
- Không vượt phạm vi repo/Drive root/GCP project đã khóa.
- Không commit secret vào repo public.

Regression guard: `tools/verify-v1428-regressions.ps1`.

## 8. Trạng thái bàn giao

**Không còn hạng mục code dang dở trong yêu cầu phiên này.**

NEXT ACTION:
1. OWNER cập nhật/test thực tế v1.4.32.
2. Test một lần trên mạng Office: thấy bản mới nhưng không tải qua Google Drive, có cảnh báo chuyển mạng.
3. Test một lần trên mạng truy cập GitHub: tải/cập nhật trực tiếp GitHub bình thường.
4. Sang phiên chat mới, chỉ cần yêu cầu: **"Đọc docs/handovers/HANDOVER_CURRENT.md, kiểm tra live rồi tiếp tục theo yêu cầu mới."**

Nếu test phát sinh lỗi, đọc log mới nhất trong thư mục Logs thuộc Drive root cố định và xử lý từ trạng thái main/release hiện tại; không quay lại logic mirror cũ.


## 9. Tối ưu ảnh upload Google Drive — v1.4.33

OWNER chốt tối ưu ảnh sau khi kiểm tra trực tiếp dung lượng Drive:
- Ảnh local gốc **không bị sửa/ghi đè**.
- Chỉ tối ưu payload trước khi upload Drive.
- Ảnh đã nhẹ (<= 1 MiB và cạnh dài <= 2560 px) giữ nguyên byte, không nén lại.
- Ảnh lớn được resize giữ nguyên tỷ lệ, cạnh dài tối đa **2560 px**.
- Payload tối ưu lưu dạng **JPEG quality 90%**; không ép cứng xuống 1 MB.
- PNG lớn được chuyển sang JPEG trước khi upload, nền trong suốt được đặt nền trắng.
- Nếu ảnh sau tối ưu không nhỏ hơn ảnh gốc thì dùng nguyên ảnh gốc.
- HEIC/WEBP hoặc codec không đọc được sẽ fallback upload nguyên file, không làm mất phiếu.
- EXIF orientation được chuẩn hóa khi phải tái mã hóa ảnh.
- Log `IMAGE_UPLOAD_OPTIMIZED` ghi dung lượng/kích thước trước-sau; không ghi nội dung ảnh hay credential.
- Dữ liệu ảnh đã có trên Drive **không bị sửa/xóa**.

Release mục tiêu: **v1.4.33**. Build/release sẽ được xác nhận lại sau GitHub Actions.
