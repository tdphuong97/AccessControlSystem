## 0.9.1

- Sửa lỗi: nhập đúng mật khẩu, máy báo "Đã kết nối" rồi khoảng 30 giây sau lại báo "Sai mật khẩu, đã dừng". Máy Hikvision chỉ giữ phiên đăng nhập vài giây; phần mềm nay tự đăng nhập lại thay vì coi đó là sai mật khẩu.
- Sửa lỗi: bấm "Kiểm tra kết nối" trên máy có nhiều người báo sai mật khẩu giữa chừng.
- Mỗi máy chấm công chỉ nhận một lệnh một lúc; hai lệnh đăng nhập cùng lúc không còn làm máy từ chối một trong hai.
- Đã thử trên hai máy DS-K1T9342MFWX thật (firmware V4.39).

## 0.9.0

- Bản đầu tiên: đăng ký khuôn mặt và vân tay cho hai máy kiểm soát ra vào Hikvision, nhập một lần và tự gửi xuống cả hai máy.
- Trình hướng dẫn lần đầu: thêm máy, kiểm tra kết nối, lấy dữ liệu có sẵn từ máy về, đặt mật khẩu sao lưu, So khớp hai máy.
- Thêm người theo 4 bước (thông tin, khuôn mặt, vân tay, xong), chụp bằng camera máy chấm công, webcam hoặc ảnh có sẵn.
- Hàng đợi gửi lên máy có tự thử lại và kiểm tra lại; máy mất mạng thì việc chờ và tự gửi khi máy online.
- So khớp: xem trước chênh lệch, bổ sung phần thiếu, xử lý người lạ; không bao giờ tự xoá.
- Khoá ra vào, mở lại, xoá hẳn người.
- Dữ liệu mã hoá trên máy tính; sao lưu tự động hằng ngày, khôi phục bằng mật khẩu sao lưu.
- Cập nhật một chạm từ GitHub Releases.
- Kèm công cụ Probe để kiểm tra máy chấm công thật tại chỗ.
- Lưu ý: chưa thử trên máy chấm công thật; ngưỡng chất lượng vân tay và một số lệnh sẽ chỉnh sau khi có báo cáo Probe.
