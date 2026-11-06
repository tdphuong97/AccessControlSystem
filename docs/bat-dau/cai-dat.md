---
description: Tải bộ cài, cài phần mềm lên máy tính, biết phần mềm lưu dữ liệu ở đâu và gỡ cài đặt khi cần.
---

# Cài đặt

**Ai làm:** người đăng ký nhân viên (thường là IT hoặc hành chính) · **Mất khoảng:** 5 phút

{% hint style="info" %}
**Trước khi bắt đầu:** chọn một máy tính Windows 10 hoặc 11 (64-bit) cùng mạng nội bộ với hai máy chấm công. Không cần quyền quản trị Windows và không cần cài thêm .NET. Phần mềm chỉ chạy trên máy đã cài, không dùng được từ điện thoại hay máy tính khác.
{% endhint %}

## Yêu cầu hệ thống

| Mục | Yêu cầu |
| --- | --- |
| Hệ điều hành | Windows 10 hoặc Windows 11, bản 64-bit |
| Quyền cài | Tài khoản Windows thường, không cần quyền quản trị |
| Mạng | Cùng mạng nội bộ với hai máy chấm công Hikvision; cần Internet khi kiểm tra và tải bản cập nhật |
| WebView2 | Giao diện dùng thành phần Microsoft Edge WebView2. Bộ cài không kèm theo thành phần này; Windows 11 và Windows 10 đã cập nhật thường có sẵn |

{% stepper %}
{% step %}
### Tải bộ cài

Mở trang [Releases của dự án trên GitHub](https://github.com/tdphuong97/AccessControlSystem/releases). Ở bản mới nhất, tải file `AccessControlSystem-Setup-<phiên bản>.exe`, ví dụ bản 0.9.0 có tên `AccessControlSystem-Setup-0.9.0.exe`.
{% endstep %}

{% step %}
### Chạy bộ cài

Chạy file vừa tải. Lần đầu Windows có thể hiện cảnh báo xanh **Windows đã bảo vệ máy tính của bạn**, vì bộ cài chưa có chữ ký số. Bấm **Thông tin thêm**, rồi bấm **Vẫn chạy**.

Chỉ phải làm một lần. Các lần cập nhật sau không hỏi lại.
{% endstep %}

{% step %}
### Chọn thiết lập bổ sung

Bấm **Tiếp theo >** cho tới trang **Các thiết lập bổ sung**. Hai ô ở đây không bắt buộc:

| Ô | Tác dụng |
| --- | --- |
| **Tạo biểu tượng ngoài màn hình** | Thêm lối tắt trên màn hình nền. |
| **Chạy cùng Windows (để tự gửi dữ liệu còn chờ)** | Mở phần mềm ẩn dưới khay hệ thống mỗi khi bạn đăng nhập Windows. Nên chọn. |

Bấm **Tiếp theo >**, rồi bấm **Cài đặt**.
{% endstep %}

{% step %}
### Mở phần mềm

Khi cài xong, giữ nguyên ô **Mở Access Control System ngay** rồi bấm **Hoàn tất**. Cửa sổ phần mềm hiện ra và mở trình hướng dẫn lần đầu.
{% endstep %}
{% endstepper %}

{% hint style="success" %}
**Kết quả:** phần mềm mở ra và dẫn bạn qua [Thiết lập lần đầu](thiet-lap-lan-dau.md). Sau này mở lại từ menu **Start** hoặc biểu tượng ngoài màn hình (nếu đã chọn).
{% endhint %}

## Phần mềm cài và lưu dữ liệu ở đâu

| Thứ | Vị trí |
| --- | --- |
| Chương trình | `%LOCALAPPDATA%\Programs\AccessControlSystem` (chỉ dành cho tài khoản Windows đang cài) |
| Dữ liệu: người, khuôn mặt, vân tay, bản sao lưu | `%LOCALAPPDATA%\AccessControlSystem` |

Dữ liệu nằm tách khỏi thư mục cài nên cập nhật hoặc gỡ cài đặt không làm mất dữ liệu. Để mở thư mục, nhấn `Windows` + `R`, nhập `%LOCALAPPDATA%\AccessControlSystem` rồi nhấn `Enter`.

## Đóng cửa sổ không tắt phần mềm

Bấm nút đóng (X) chỉ ẩn cửa sổ; phần mềm vẫn chạy ở khay hệ thống (gần đồng hồ) để gửi tiếp dữ liệu còn chờ xuống máy. Lần đầu bạn sẽ thấy thông báo **Phần mềm vẫn chạy ở khay hệ thống để gửi dữ liệu còn chờ.**

- Để mở lại cửa sổ: bấm biểu tượng ở khay, hoặc bấm chuột phải rồi chọn **Mở**. Mở phần mềm lần nữa từ menu **Start** cũng chỉ đưa cửa sổ đang chạy lên phía trước, không mở bản thứ hai.
- Để tắt hẳn: bấm chuột phải biểu tượng ở khay, chọn **Thoát**. Khi đó dữ liệu chờ gửi không được gửi cho tới lần mở sau.

## Cập nhật

Phần mềm tự báo khi có bản mới. Xem [Cập nhật phần mềm](../may-cham-cong/cap-nhat-phan-mem.md). Bạn cũng có thể tải bộ cài mới ở trang Releases và chạy đè lên bản cũ; dữ liệu được giữ nguyên.

## Lỗi thường gặp

<details>

<summary>Windows báo "Windows đã bảo vệ máy tính của bạn" và không có nút chạy</summary>

Cảnh báo ban đầu chỉ có nút **Không chạy**. Bấm **Thông tin thêm** để nút **Vẫn chạy** hiện ra.

</details>

<details>

<summary>Phần mềm không hiện cửa sổ sau khi cài</summary>

Có thể máy chưa có thành phần Microsoft Edge WebView2 Runtime. Khi đó cửa sổ phần mềm trống, hoặc hiện thông báo **Phần mềm gặp lỗi…** mà phần **Chi tiết** nhắc tới WebView2. Tải bản **Evergreen** của Microsoft Edge WebView2 Runtime từ trang của Microsoft, cài vào máy, rồi mở lại phần mềm.

</details>

## Gỡ cài đặt

<details>

<summary>Gỡ phần mềm (dữ liệu được giữ lại)</summary>

1. Mở **Settings** > **Apps** > **Installed apps** (Windows 10: **Settings** > **Apps** > **Apps & features**).
2. Tìm **Access Control System**, bấm **⋯** rồi chọn **Uninstall**.

Bộ gỡ cài đặt không xoá thư mục `%LOCALAPPDATA%\AccessControlSystem`, nên người, khuôn mặt, vân tay và bản sao lưu vẫn còn. Cài lại phần mềm là dùng tiếp được. Hai máy chấm công không bị ảnh hưởng.

Muốn xoá hết dữ liệu, tự xoá thư mục `%LOCALAPPDATA%\AccessControlSystem` sau khi gỡ. Việc này không hoàn tác được; hãy xem [Dữ liệu cá nhân và quyền riêng tư](../tham-khao/du-lieu-ca-nhan.md) trước.

</details>
