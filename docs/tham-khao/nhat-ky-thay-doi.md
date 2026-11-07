---
description: Các phiên bản đã phát hành và thay đổi trong từng bản.
---

# Nhật ký thay đổi

{% updates format="full" %}
{% update date="2026-11-07" %}
## Phiên bản 0.9.5

* Mới: nút **Hướng dẫn** ở góc trên bên phải (phím `F1`) mở tài liệu này ngay trong phần mềm, đúng trang của màn hình đang dùng. Không cần mạng.
* Hướng dẫn bổ sung trang [Lấy dữ liệu từ máy](../may-cham-cong/lay-du-lieu-tu-may.md) và bảng "Bạn cần bấm gì để đồng bộ".
* Bỏ khoảng trống thừa phía trên tiêu đề các màn hình.
{% endupdate %}

{% update date="2026-11-07" %}
## Phiên bản 0.9.4

* Danh sách người dùng chia trang: chọn 20, 50, 100 hoặc **Tất cả** người mỗi trang (mặc định 50), có nút **Trước** / **Sau**. Phần mềm nhớ lựa chọn trên máy tính này.
{% endupdate %}

{% update date="2026-11-07" %}
## Phiên bản 0.9.3

* Sửa lỗi: So khớp báo nhầm "thiếu vân tay" cho người mà máy vẫn có đủ ngón, mỗi lần chạy ra một danh sách khác. Phần mềm nay đọc trọn vân tay từng người một mạch, và không báo thiếu khi số ngón đọc được ít hơn số máy báo.
* Lấy dữ liệu từ máy vẫn chạy khi chuyển tab: quay lại vẫn thấy tiến độ, tab **Người dùng** hiện dòng đang lấy dữ liệu, lấy xong danh sách tự cập nhật.
* Không chạy hai lần lấy dữ liệu cùng lúc. So khớp chờ lấy dữ liệu xong rồi mới so.
* Đã thử trên hai máy thật: So khớp chạy hai lần liền cho cùng kết quả.
{% endupdate %}

{% update date="2026-11-07" %}
## Phiên bản 0.9.2

* Mới: mục **Lấy dữ liệu từ máy** trên tab **Máy chấm công & cập nhật**, dùng được bất cứ lúc nào.
* So khớp 2 máy: khi phần mềm chưa có ai, đề nghị lấy dữ liệu từ máy trước thay vì liệt kê mọi người trên máy là người lạ.
* Trạng thái máy đổi từ "Đang kết nối" thành **Đã kết nối**.
* Sau khi cập nhật, phần mềm tự xoá bộ cài đã tải về.
{% endupdate %}

{% update date="2026-11-07" %}
## Phiên bản 0.9.1

* Sửa lỗi: nhập đúng mật khẩu, máy báo đã kết nối rồi khoảng 30 giây sau lại báo **Sai mật khẩu, đã dừng**.
* Sửa lỗi: **Kiểm tra kết nối** trên máy có nhiều người báo sai mật khẩu giữa chừng.
* Mỗi máy chỉ nhận một lệnh một lúc.
* Thử lần đầu trên hai máy DS-K1T9342MFWX thật (phần mềm máy V4.39).
{% endupdate %}

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
