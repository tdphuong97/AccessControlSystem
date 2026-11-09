---
description: Dùng chung một danh sách người, khuôn mặt và vân tay cho nhiều máy tính, ví dụ máy của người làm chính và máy của người làm thay.
---

# Dữ liệu chung

**Ai làm:** Người quản lý phần mềm · **Mất khoảng:** 5 phút mỗi máy tính

Bình thường mỗi máy tính cài phần mềm giữ một danh sách riêng. Bật **Dữ liệu chung** thì các máy tính dùng chung một danh sách: ai mở phần mềm cũng thấy đúng danh sách mới nhất và làm được mọi việc như thường (thêm người, chụp mặt, quét vân tay, So khớp, gửi xuống máy chấm công).

Dữ liệu chung nằm trên Cloudflare (tài khoản của Viên Ba). Mọi thứ được **mã hoá ngay trên máy tính** bằng **mật khẩu dữ liệu chung** trước khi gửi lên, nên Cloudflare không đọc được tên, mã nhân viên, ảnh mặt, vân tay hay mật khẩu máy chấm công.

{% hint style="info" %}
**Trước khi bắt đầu:** cập nhật phần mềm trên mọi máy tính lên bản 0.9.15 trở lên, đặt mật khẩu sao lưu trên mỗi máy (xem [Sao lưu và khôi phục](sao-luu-khoi-phuc.md)), và xin **mã kết nối** từ người quản trị (một dòng bắt đầu bằng `acs1:`).
{% endhint %}

## Bật trên máy tính đầu tiên

Làm trên máy tính đang có danh sách đầy đủ nhất (thường là máy của người làm chính).

{% stepper %}
{% step %}
### Mở mục Dữ liệu chung

Mở tab **Máy chấm công & cập nhật**, kéo xuống mục **Dữ liệu chung**, bấm **Kết nối dữ liệu chung**.
{% endstep %}

{% step %}
### Nhập mã kết nối

Dán nguyên dòng mã kết nối rồi bấm **Tiếp tục**. Phần mềm báo "Chưa có dữ liệu chung" và cho biết sẽ đưa bao nhiêu người lên.
{% endstep %}

{% step %}
### Đặt mật khẩu dữ liệu chung

Nhập mật khẩu (từ 8 ký tự) hai lần rồi bấm **Kết nối**. Phần mềm sao lưu máy này rồi đưa toàn bộ dữ liệu lên. Xong sẽ hiện "Đã bật dữ liệu chung".
{% endstep %}
{% endstepper %}

{% hint style="danger" %}
Mất mật khẩu dữ liệu chung thì máy tính mới không tham gia được, và không ai lấy lại được. Hãy ghi lại ở nơi an toàn ngay khi đặt. Máy đã kết nối thì không cần nhập lại.
{% endhint %}

## Tham gia trên máy tính khác

1. Trên máy kia (ví dụ máy của người làm thay), mở mục **Dữ liệu chung**, bấm **Kết nối dữ liệu chung**.
2. Dán cùng mã kết nối, bấm **Tiếp tục**. Phần mềm báo danh sách trên máy này sẽ được thay bằng dữ liệu chung.
3. Nhập mật khẩu dữ liệu chung, bấm **Kết nối**. Phần mềm sao lưu máy này trước, rồi lấy dữ liệu chung về và báo đã lấy bao nhiêu người.
4. Chạy [So khớp 2 máy](so-khop-2-may.md) một lần cho chắc.

## Dùng hằng ngày

- Mỗi thay đổi được gửi lên trong khoảng 1 giây; máy kia nhận trong khoảng 15 giây.
- **Máy tính nào đang mở cửa sổ phần mềm thì máy đó gửi xuống máy chấm công.** Máy chỉ chạy dưới khay đồng hồ thì nhường. Mục Dữ liệu chung ghi "Máy này đang gửi xuống máy chấm công" hoặc tên máy đang gửi.
- Hai người lỡ sửa cùng một người: **bản sửa sau được giữ**, và [Nhật ký](xem-nhat-ky.md) ghi lại thay đổi nào bị thay. Nhật ký cũng ghi tên máy tính đã làm từng việc.
- Mất Internet: phần mềm vẫn chạy bằng dữ liệu trên máy, vẫn gửi xuống máy chấm công. Mục Dữ liệu chung ghi số thay đổi đang giữ; có mạng lại sẽ tự gửi.

Các nút trong mục: **Đồng bộ ngay** (không cần chờ), **Kiểm tra kết nối**, **Đổi mật khẩu dữ liệu chung**.

## Tắt và bật lại trên một máy

Bỏ dấu ở ô **Dùng dữ liệu chung** để máy đó dùng riêng như trước, giữ nguyên dữ liệu đang có.

Đánh dấu lại để bật: không cần nhập lại mã hay mật khẩu. Phần mềm sao lưu máy đó trước rồi lấy dữ liệu chung về. Những gì sửa trên máy đó lúc đang tắt sẽ không được đưa lên.

**Nâng cao › Quên kết nối** xoá mã kết nối và khoá khỏi máy đó; muốn dùng lại phải nhập mã và mật khẩu.

## Khôi phục khi đang dùng dữ liệu chung

Khôi phục một bản sao lưu lúc đang bật Dữ liệu chung sẽ đưa **dữ liệu của cả hai máy** về như bản sao lưu. Hộp thoại ghi rõ số người hiện có và số người trong bản sao lưu, và cần bấm **Khôi phục** lần nữa để xác nhận. Muốn chỉ một máy quay về thì tắt Dữ liệu chung trên máy đó trước rồi mới khôi phục. Xem [Sao lưu và khôi phục](sao-luu-khoi-phuc.md).
