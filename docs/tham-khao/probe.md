---
description: Chạy công cụ Probe một lần tại chỗ để kiểm tra các lệnh phần mềm sẽ dùng trên máy chấm công thật, rồi gửi báo cáo lại.
---

# Kiểm tra máy thật bằng Probe

**Ai làm:** người phụ trách IT, có mặt tại chỗ với máy chấm công · **Mất khoảng:** 10 phút

Bản 0.9.0 chưa được thử trên máy chấm công thật. Probe (`AccessControlSystem-Probe.exe`) là công cụ dòng lệnh đi kèm bản phát hành, dùng một lần để kiểm tra từng lệnh phần mềm sẽ dùng: đọc thông tin máy, tạo người, ghi khuôn mặt, ghi vân tay, đọc lại. Kết quả là một file báo cáo để gửi lại cho người phát triển.

{% hint style="success" %}
Probe chỉ tạo, sửa và xoá duy nhất một người thử mã `99999` (tên **TEST ACS**) rồi tự xoá khi xong, kể cả khi bạn nhấn `Ctrl` + `C`. Người thật trên máy không bị đụng tới. Nếu mã `99999` đang thuộc về người khác, Probe dừng lại và không đổi gì.
{% endhint %}

{% hint style="info" %}
**Trước khi bắt đầu:** máy tính và máy chấm công cùng mạng. Có sẵn địa chỉ IP, tài khoản và mật khẩu của máy (và của máy thứ hai nếu muốn thử cả hai). Một người đứng trước máy để chụp mặt và đặt ngón tay lên máy quét. Ngoài ra bạn tắt phần mềm Access Control System trước khi chạy để tránh hai chương trình cùng đăng nhập máy một lúc.
{% endhint %}

{% stepper %}
{% step %}
### Tải Probe

Ở trang [Releases](https://github.com/tdphuong97/AccessControlSystem/releases), tải file `AccessControlSystem-Probe.exe` và đặt vào một thư mục trên máy tính.
{% endstep %}

{% step %}
### Mở cửa sổ dòng lệnh

Mở PowerShell hoặc Command Prompt tại thư mục có file Probe.
{% endstep %}

{% step %}
### Chạy Probe

Gõ lệnh sau, thay địa chỉ và tài khoản bằng của máy thật:

{% code title="Một máy" %}
```
AccessControlSystem-Probe.exe --host http://192.168.1.201 --user admin --pass ******
```
{% endcode %}

Có máy thứ hai thì thêm `--peer`, `--peer-user`, `--peer-pass`. Thêm `--out` để chọn thư mục lưu báo cáo (mặc định là thư mục hiện tại):

{% code title="Hai máy" overflow="wrap" %}
```
AccessControlSystem-Probe.exe --host http://192.168.1.201 --user admin --pass ****** --peer http://192.168.1.202 --peer-user admin --peer-pass ****** --out C:\Probe
```
{% endcode %}

Thay `******` bằng mật khẩu thật của máy. Đã có `--peer` thì phải có đủ `--peer-user` và `--peer-pass`, nếu thiếu Probe báo lỗi và in hướng dẫn. Chạy với `--help` để xem lại hướng dẫn.
{% endstep %}

{% step %}
### Làm theo lời nhắc

Probe nhắc hai lần, mỗi lần bạn bấm `Enter` sau khi người thử đã sẵn sàng:

1. **Mời người thử đứng trước máy rồi bấm Enter để chụp khuôn mặt.** Người thử đứng trước camera của máy.
2. **Mời người thử đứng trước máy rồi bấm Enter để lấy vân tay (ngón 1).** Người thử đặt ngón tay lên máy quét.

Giữa hai lần nhắc, Probe tự chạy các bước còn lại. Đừng đóng cửa sổ giữa chừng. Muốn dừng, nhấn `Ctrl` + `C`: Probe báo **Đang dừng và dọn dẹp...** và vẫn xoá người thử.
{% endstep %}

{% step %}
### Đọc kết quả và gửi báo cáo

Khi xong, Probe in toàn bộ báo cáo ra màn hình và dòng **Đã ghi báo cáo:** kèm đường dẫn file `probe-report-yyyyMMdd-HHmm.txt` (ví dụ `probe-report-20261106-1430.txt`). Mật khẩu trong báo cáo đã được che bằng `***`. Gửi file này cho người phát triển: mở [trang Issues của dự án trên GitHub](https://github.com/tdphuong97/AccessControlSystem/issues), tạo issue mới và đính kèm file. Trang Issues là công khai, nên mở file ra xem trước và xoá những gì bạn không muốn đăng, ví dụ địa chỉ IP.
{% endstep %}
{% endstepper %}

## Cách đọc báo cáo

Mỗi dòng của báo cáo có dạng `[OK]` hoặc `[LỖI]`, tên bước, thời gian (ms) và chi tiết. Các bước gồm thông tin và khả năng thiết bị, đếm số người (chỉ đếm, không ghi lại người nào), tạo người thử, ghi và đọc lại khuôn mặt, lấy và ghi vân tay, và nếu có máy thứ hai thì ghi sang máy đó và kiểm tra lại. Dòng cuối cùng của các bước dọn dẹp xác nhận `99999` đã biến mất khỏi máy.

Khi chạy xong, Probe in một trong hai dòng:

- **Tất cả các bước đều OK.** Bạn không cần làm gì thêm ngoài gửi báo cáo.
- **Có bước LỖI. Hãy gửi file báo cáo cho người hỗ trợ.** Vẫn gửi báo cáo; người phát triển sẽ chỉnh phần mềm theo đó.

{% hint style="warning" %}
Nếu có dòng **99999 VẪN CÒN trên máy sau khi xoá — cần xoá tay**, hãy vào menu của máy chấm công và xoá người có mã `99999` (tên TEST ACS).
{% endhint %}

## Lỗi thường gặp

<details>

<summary>mã 99999 đang thuộc về một người khác</summary>

Probe thấy một người khác dùng mã `99999` trên máy nên dừng, không đổi gì. Báo cho người hỗ trợ; không chạy tiếp cho tới khi có hướng dẫn.

</details>

<details>

<summary>Tham số không hợp lệ</summary>

Có chỗ gõ sai tên tuỳ chọn hoặc thiếu giá trị. Mỗi tuỳ chọn bắt đầu bằng `--` và có giá trị đi liền sau. Chạy `AccessControlSystem-Probe.exe --help` để xem lại.

</details>

<details>

<summary>Máy báo sai mật khẩu hoặc không kết nối được</summary>

Xem [Xử lý sự cố](xu-ly-su-co.md). Đừng chạy lại nhiều lần với mật khẩu sai: máy Hikvision khoá địa chỉ IP khoảng 30 phút.

</details>
