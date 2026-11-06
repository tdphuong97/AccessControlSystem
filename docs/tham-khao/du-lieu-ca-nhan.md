---
description: Phần mềm lưu dữ liệu gì, ở đâu, bảo vệ thế nào và bạn nên làm gì để dùng dữ liệu sinh trắc đúng cách.
---

# Dữ liệu cá nhân và quyền riêng tư

Trang này tóm tắt cách phần mềm xử lý dữ liệu, bằng lời dễ hiểu. Đây không phải tư vấn pháp lý.

## Dữ liệu nằm ở đâu

- Mọi dữ liệu (danh sách người, ảnh khuôn mặt, mẫu vân tay, mật khẩu máy chấm công) nằm trên máy tính đã cài phần mềm, trong `%LOCALAPPDATA%\AccessControlSystem`.
- Mật khẩu máy, ảnh khuôn mặt và mẫu vân tay được mã hoá. Khoá mã hoá được Windows bảo vệ theo tài khoản Windows đang dùng.
- File sao lưu được mã hoá bằng **mật khẩu sao lưu** do bạn đặt. Mất mật khẩu thì không mở được bản sao lưu.
- Phần mềm không mở cổng mạng và không có máy chủ riêng. Không có tài khoản đám mây, không có thống kê sử dụng, không có báo cáo lỗi tự gửi đi.

## Phần mềm kết nối tới đâu

Chỉ hai nơi:

1. **Hai máy chấm công Hikvision** do bạn khai báo, trong mạng nội bộ, để gửi và đọc người, khuôn mặt, vân tay.
2. **GitHub** (`github.com`), để kiểm tra và tải bản cập nhật. Phần mềm chỉ tải file mô tả phiên bản và bộ cài, không gửi dữ liệu về người hay máy chấm công. Bạn tắt được việc tự kiểm tra bằng ô **Tự kiểm tra bản mới mỗi ngày** ở tab **Máy chấm công & cập nhật**.

## Dữ liệu sinh trắc là dữ liệu nhạy cảm

Ảnh khuôn mặt và vân tay là dữ liệu cá nhân nhạy cảm theo Nghị định 13/2023/NĐ-CP và Luật Bảo vệ dữ liệu cá nhân (có hiệu lực từ 01/01/2026). Khuyến nghị:

- Có giấy đồng ý của nhân viên trước khi đăng ký khuôn mặt hoặc vân tay, nêu rõ mục đích (chấm công, kiểm soát ra vào).
- Chỉ quản trị viên được dùng máy tính cài phần mềm. Đặt mật khẩu Windows và khoá màn hình khi rời máy.
- **Xoá hẳn** người đã nghỉ việc khi không còn cần giữ. Phần mềm nhắc ở tab **Người dùng** khi có người đã khoá quá 6 tháng. Bản sao lưu cũ vẫn có thể còn dữ liệu của họ. Xem [Xoá hẳn một người](../nguoi-dung/xoa-han.md).
- Cất file sao lưu ở nơi an toàn.

Các yêu cầu cụ thể về giấy tờ, thời hạn lưu và thông báo nên hỏi bộ phận pháp chế hoặc nhân sự của bạn.

## Liên quan

- [Khoá ra vào và mở lại](../nguoi-dung/khoa-va-mo-lai.md)
- [Sao lưu và khôi phục](../may-cham-cong/sao-luu-khoi-phuc.md)
