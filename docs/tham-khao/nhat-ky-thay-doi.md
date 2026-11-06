---
description: Các phiên bản đã phát hành và thay đổi trong từng bản.
---

# Nhật ký thay đổi

{% updates format="full" %}
{% update date="2026-11-06" %}
## Phiên bản 0.9.0

Bản đầu tiên.

* Đăng ký khuôn mặt và vân tay cho hai máy kiểm soát ra vào Hikvision: nhập một lần, phần mềm tự gửi xuống cả hai máy.
* Trình hướng dẫn lần đầu: thêm máy, kiểm tra kết nối, lấy dữ liệu có sẵn từ máy về, đặt mật khẩu sao lưu, So khớp hai máy.
* Thêm người theo 4 bước (thông tin, khuôn mặt, vân tay, xong); chụp bằng camera máy chấm công, webcam hoặc ảnh có sẵn.
* Hàng đợi gửi xuống máy có tự thử lại và kiểm tra lại; máy mất mạng thì việc chờ và tự gửi khi máy online lại.
* So khớp: xem trước chênh lệch, bổ sung phần thiếu, xử lý người lạ; không bao giờ tự xoá.
* Khoá ra vào, mở lại, xoá hẳn người.
* Dữ liệu mã hoá trên máy tính; sao lưu tự động hằng ngày, khôi phục bằng mật khẩu sao lưu.
* Cập nhật một chạm từ GitHub Releases.
* Kèm công cụ Probe để kiểm tra máy chấm công thật tại chỗ.

**Lưu ý:** chưa thử trên máy chấm công thật. Ngưỡng chất lượng vân tay và một số lệnh sẽ được chỉnh sau khi có báo cáo Probe.
{% endupdate %}
{% endupdates %}
