---
description: Những việc phần mềm không tự làm được, vì sao, và bạn cần làm gì trong từng trường hợp.
---

# Những trường hợp không làm được

Phần mềm này không làm được mọi việc. Trang này liệt kê từng trường hợp, lý do và cách xử lý, để bạn biết trước chứ không bất ngờ giữa buổi đăng ký nhân viên.

| Trường hợp | Vì sao | Phần mềm làm gì, bạn cần làm gì |
| --- | --- | --- |
| Vân tay không chép sang máy kia được | Vân tay chỉ dùng chung giữa các máy có cùng loại cảm biến | Phần mềm báo máy nào không nhận: **Cần quét** ngón đó **trực tiếp ở** tên máy. Quét ngón đó ở máy kia. Nên kiểm tra model của hai máy ở bước **Kiểm tra** khi thiết lập. Xem [Lấy vân tay](../nguoi-dung/lay-van-tay.md) |
| Người cũ không tải được ảnh mặt về | Một số máy chỉ giữ mẫu nhận diện, không cho tải ảnh gốc | Phần mềm báo người đó cần chụp lại một lần; người đó vẫn vào được máy đang có mặt. Chụp lại nếu muốn gửi sang máy kia |
| Máy từ chối ảnh mặt | Ảnh mờ, ngược sáng, mặt nhỏ hoặc nghiêng, hoặc quá dung lượng | Phần mềm tự cắt và nén ảnh (tối đa 200 KB, định dạng JPEG). Vẫn bị từ chối thì báo kèm lý do; chụp lại ảnh rõ mặt, đủ sáng, nhìn thẳng |
| Máy cũ không nhận lệnh chụp mặt hoặc quét vân tay qua mạng | Tuỳ model và firmware | Phần mềm báo **Máy này không hỗ trợ**. Với mặt, dùng webcam hoặc ảnh có sẵn. Với vân tay, đăng ký trực tiếp trên menu của máy; người nào chưa có trong phần mềm sẽ hiện là người lạ khi chạy [So khớp 2 máy](../may-cham-cong/so-khop-2-may.md), chọn **Nhập vào app** để đọc về |
| Máy tính tắt hoặc phần mềm không chạy | Hàng đợi chỉ gửi khi phần mềm chạy | Phần mềm chạy nền ở khay và khởi động cùng Windows (nếu bạn chọn khi cài). Việc chờ vẫn còn và gửi tiếp khi phần mềm mở lại |
| Máy chấm công bị reset hoặc thay máy mới | Máy mất hết dữ liệu | Chạy **So khớp 2 máy**: phần mềm gửi lại toàn bộ từ bản gốc |
| Có người thêm hoặc sửa thẳng trên máy, hoặc bằng iVMS-4200 | Phần mềm không biết cho tới khi So khớp | So khớp hiện họ là người lạ để bạn chọn **Nhập vào app**, **Gỡ khỏi máy** hoặc **Bỏ qua**. Nên chỉ dùng một công cụ để thêm người |
| Một người chỉ được vào một số máy (ví dụ chỉ tầng trệt) | Phần mềm gửi mỗi người xuống tất cả các máy, chưa có chế độ chọn máy riêng cho từng người | Không thêm người đó vào phần mềm: đăng ký trực tiếp trên máy hoặc bằng iVMS-4200, và để **Bỏ qua** khi họ hiện là người lạ trong So khớp |
| Sai mật khẩu máy | Hikvision khoá địa chỉ IP khoảng 30 phút sau vài lần sai | Phần mềm dừng ngay sau lần sai đầu, không tự thử lại. Sửa mật khẩu rồi thử lại |
| Hỏng máy tính hoặc cài lại Windows | Khoá mã hoá gắn với tài khoản Windows cũ | Cài phần mềm lại, rồi khôi phục từ file sao lưu cùng mật khẩu sao lưu. Xem [Sao lưu và khôi phục](../may-cham-cong/sao-luu-khoi-phuc.md) |
| Cảnh báo **Windows đã bảo vệ máy tính của bạn** khi cài lần đầu | Bộ cài chưa có chữ ký số | Bấm **Thông tin thêm** > **Vẫn chạy** một lần. Xem [Cài đặt](../bat-dau/cai-dat.md) |
| Dùng từ điện thoại hoặc trình duyệt | Phần mềm chỉ chạy trên máy tính đã cài | Không hỗ trợ, cố ý để không mở cổng mạng |
| Nhiều máy tính cùng dùng | Mỗi máy tính có danh sách riêng | Bật [Dữ liệu chung](../may-cham-cong/du-lieu-chung.md) (cần Internet để đồng bộ) |
| Pháp lý về dữ liệu sinh trắc | Ảnh mặt và vân tay là dữ liệu cá nhân nhạy cảm | Xem [Dữ liệu cá nhân và quyền riêng tư](du-lieu-ca-nhan.md) |
| Máy đầy bộ nhớ | Mỗi model có giới hạn số người, mặt, vân tay | Phần mềm báo máy đầy (người, khuôn mặt hoặc vân tay). Xoá bớt người không còn dùng trên máy rồi thử lại |

## Giới hạn khác trong phần mềm

- Họ tên dài tối đa 32 ký tự, vì máy chấm công không nhận tên dài hơn.
- Mã nhân viên chỉ gồm chữ và số, tối đa 32 ký tự.
- Phần mềm không bao giờ tự xoá người khỏi máy; mọi lần gỡ đều do bạn chọn. Ngoại lệ có thông báo trước: người đã **Khoá ra vào** được gỡ khỏi máy, nhưng dữ liệu vẫn giữ trong phần mềm.
- Phần mềm đã chạy trên hai máy DS-K1T9342MFWX thật (phần mềm máy V4.39): kết nối, lấy dữ liệu 157 người kèm khuôn mặt và vân tay, So khớp, hình camera trực tiếp (cả lúc máy đang chụp). Máy nhận lệnh chụp khuôn mặt và quét vân tay qua mạng; một lần đăng ký trọn vẹn với người thật (chụp được mặt, quét được ngón) chưa được ghi nhận. Xem [Kiểm tra máy thật bằng Probe](probe.md).
