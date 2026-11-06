# Quyền riêng tư

Tài liệu này mô tả phần mềm Access Control System xử lý dữ liệu thế nào. Đây không phải tư vấn pháp lý.

## Dữ liệu nằm ở đâu

- Mọi dữ liệu (danh sách người, ảnh mặt, mẫu vân tay, mật khẩu máy chấm công) nằm **trên máy tính đã cài phần mềm**, trong `%LOCALAPPDATA%\AccessControlSystem`.
- Mật khẩu máy, ảnh mặt và mẫu vân tay được **mã hoá** (AES-GCM). Khoá mã hoá được Windows DPAPI bảo vệ theo tài khoản Windows đang dùng.
- File sao lưu được mã hoá bằng mật khẩu sao lưu do quản trị viên đặt. Mất mật khẩu này thì không mở được bản sao lưu.
- Phần mềm **không mở cổng mạng** và không có máy chủ riêng. Không có tài khoản đám mây, không có thống kê sử dụng, không có báo cáo lỗi tự gửi đi.

## Phần mềm kết nối tới đâu

Chỉ hai nơi:

1. **Hai máy chấm công Hikvision** do bạn khai báo, trong mạng nội bộ, để gửi và đọc người, mặt, vân tay.
2. **GitHub** (`github.com`), để kiểm tra và tải bản cập nhật. Yêu cầu này chỉ tải file mô tả phiên bản và bộ cài; không gửi dữ liệu về người hay máy chấm công. Có thể tắt việc tự kiểm tra trong phần cập nhật.

## Dữ liệu sinh trắc là dữ liệu nhạy cảm

Ảnh khuôn mặt và vân tay là dữ liệu cá nhân nhạy cảm theo Nghị định 13/2023/NĐ-CP và Luật Bảo vệ dữ liệu cá nhân (có hiệu lực từ 01/01/2026). Khuyến nghị:

- Có **giấy đồng ý** của nhân viên trước khi đăng ký khuôn mặt hoặc vân tay, nêu rõ mục đích (chấm công, kiểm soát ra vào).
- Chỉ quản trị viên được dùng máy tính cài phần mềm; đặt mật khẩu Windows và khoá màn hình.
- **Xoá hẳn** người đã nghỉ việc khi không còn cần giữ (phần mềm gợi ý với người đã khoá quá 6 tháng). Nhớ rằng bản sao lưu cũ vẫn có thể chứa dữ liệu của họ.
- Cất file sao lưu ở nơi an toàn.

Các yêu cầu cụ thể về giấy tờ, thời hạn lưu và thông báo nên hỏi bộ phận pháp chế hoặc nhân sự của bạn. Tài liệu này không thay thế tư vấn pháp lý.
