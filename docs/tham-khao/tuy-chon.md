---
description: Tất cả tuỳ chọn trên tab Máy chấm công & cập nhật, giá trị mặc định và ý nghĩa.
---

# Tuỳ chọn

Mở tab **Máy chấm công & cập nhật** (tab thứ hai ở đầu cửa sổ). Tab chia thành năm mục, từ trên xuống dưới như bảng dưới đây.

## Cập nhật phần mềm

| Tuỳ chọn | Mặc định | Ý nghĩa |
| --- | --- | --- |
| **Kiểm tra bản mới** | Không áp dụng | Nút kiểm tra bản mới ngay. Chỉ hiện khi chưa có bản mới. |
| **Cập nhật ngay** | Không áp dụng | Nút tải và cài bản mới. Chỉ hiện khi có bản mới. Phần mềm tự đóng rồi mở lại. |

Xem [Cập nhật phần mềm](../may-cham-cong/cap-nhat-phan-mem.md).

## Máy chấm công

Nút **Thêm máy** ở đầu mục mở biểu mẫu. Mỗi máy có thẻ riêng với các nút **Kiểm tra kết nối**, **Sửa** (hoặc **Sửa mật khẩu** khi máy đang dừng vì sai mật khẩu) và **Xoá máy**.

| Tuỳ chọn | Mặc định | Ý nghĩa |
| --- | --- | --- |
| **Tên máy** | Trống (bắt buộc) | Tên gọi trong phần mềm, ví dụ `Máy tầng trệt`. |
| **Địa chỉ máy** | Trống (bắt buộc) | Số IP hoặc tên máy, không có `http://`. Đổi địa chỉ làm phần mềm quên những gì máy cũ đã xác nhận và nhắc chạy So khớp. |
| **Tài khoản** | `admin` | Tài khoản đăng nhập máy. |
| **Mật khẩu** | Trống (bắt buộc khi thêm) | Mật khẩu máy, lưu mã hoá trên máy tính này. Khi sửa, để trống là giữ mật khẩu cũ. |
| **Số cửa** | 1 | Số cửa của máy, từ 1 đến 8. |
| **Dùng máy này để chụp mặt (máy chụp mặc định)** | Không chọn | Máy dùng camera chụp khuôn mặt khi thêm người. Chỉ một máy được chọn; chọn máy này thì máy khác tự bỏ. |
| **Dùng máy này để quét vân tay** | Chọn | Máy dùng để lấy vân tay. |

Xem [Thêm và sửa máy chấm công](../may-cham-cong/them-sua-may.md).

## So khớp 2 máy

| Tuỳ chọn | Mặc định | Ý nghĩa |
| --- | --- | --- |
| **So khớp 2 máy** | Không áp dụng | Nút mở màn hình So khớp. Trên màn hình đó, mỗi người lạ có ô chọn **Bỏ qua** (mặc định), **Nhập vào app** hoặc **Gỡ khỏi máy**. |

Xem [So khớp 2 máy](../may-cham-cong/so-khop-2-may.md).

## Sao lưu

| Tuỳ chọn | Mặc định | Ý nghĩa |
| --- | --- | --- |
| **Đặt mật khẩu sao lưu** / **Đổi mật khẩu** | Chưa đặt | Mật khẩu mã hoá file sao lưu, từ 8 ký tự, nhập hai lần, nút **Lưu mật khẩu**. Mất mật khẩu thì không mở được bản sao lưu. |
| **Sao lưu ngay** | Không áp dụng | Tạo một bản sao lưu ngay. Mờ khi chưa đặt mật khẩu. |
| Sao lưu tự động | Bật khi đã đặt mật khẩu | Khi mở phần mềm nếu bản gần nhất quá 24 giờ, và trước mỗi lần cập nhật. Không có công tắc riêng. Giữ 14 bản. |
| **Chép thêm một bản ra thư mục khác (USB hoặc ổ mạng)** | Trống | Đường dẫn đầy đủ, ví dụ `E:\SaoLuuACS`. Mỗi lần sao lưu chép thêm một bản vào đây. Nút **Lưu thư mục** và **Bỏ thư mục**. |
| **Khôi phục** (từng dòng trong danh sách) | Không áp dụng | Khôi phục từ bản sao lưu trên máy này, cần mật khẩu sao lưu. |
| **Chọn từ thư mục khác** | Không áp dụng | Khôi phục từ file `.acsbak` trong thư mục khác: nhập thư mục, bấm **Xem**, chọn file. Dùng khi chuyển sang máy tính mới. |

Xem [Sao lưu và khôi phục](../may-cham-cong/sao-luu-khoi-phuc.md).

## Khác

| Tuỳ chọn | Mặc định | Ý nghĩa |
| --- | --- | --- |
| **Tự kiểm tra bản mới mỗi ngày** | Chọn | Phần mềm tự tìm bản mới 1 phút sau khi mở, rồi cứ 20 giờ một lần. Bỏ chọn để tắt; vẫn bấm **Kiểm tra bản mới** bằng tay được. |
| **Tự chạy cùng Windows (để việc chờ gửi lên máy không bị bỏ lỡ)** | Bỏ chọn trong phần mềm; bộ cài có ô **Chạy cùng Windows** riêng | Mở phần mềm khi đăng nhập Windows để hàng đợi tiếp tục gửi. Nên bật. |
| **Xem nhật ký thao tác** | Không áp dụng | Nút mở màn hình **Nhật ký**, xem [Xem nhật ký](../may-cham-cong/xem-nhat-ky.md). |

Các lựa chọn trong mục **Khác** có hiệu lực ngay khi bạn chọn hoặc bỏ chọn, không có nút lưu.
