---
description: Câu trả lời cho những thắc mắc người đăng ký nhân viên hay hỏi nhất.
---

# Câu hỏi thường gặp

## Cài đặt và sử dụng hằng ngày

<details>

<summary>Tôi dùng phần mềm từ điện thoại hoặc từ máy tính khác được không?</summary>

Không. Phần mềm chỉ chạy trên máy tính đã cài và không mở cổng mạng, nên không truy cập được từ nơi khác. Đây là chủ ý để dữ liệu sinh trắc an toàn hơn.

</details>

<details>

<summary>Hai máy tính cùng chạy phần mềm được không?</summary>

Được, khi bật [Dữ liệu chung](../may-cham-cong/du-lieu-chung.md) (bản 0.9.15 trở lên): các máy tính dùng chung một danh sách, máy nào đang mở phần mềm thì máy đó gửi xuống máy chấm công. Chưa bật thì không nên: mỗi máy tính giữ một bản riêng, hai bản sẽ lệch và ghi đè nhau trên máy chấm công. Chỉ chuyển sang máy tính khác thì dùng [Sao lưu và khôi phục](../may-cham-cong/sao-luu-khoi-phuc.md).

</details>

<details>

<summary>Máy tính tắt thì dữ liệu có bị mất không?</summary>

Không mất. Việc chưa gửi xuống máy chấm công nằm chờ trong hàng đợi. Chúng chỉ được gửi khi phần mềm đang chạy, nên hãy chọn **Chạy cùng Windows** khi cài. Bật máy tính và mở phần mềm là việc chờ tự gửi tiếp. Xem [Theo dõi việc gửi xuống máy](../may-cham-cong/theo-doi-gui-xuong-may.md).

</details>

<details>

<summary>Tôi bấm đóng cửa sổ, phần mềm có tắt không?</summary>

Không. Phần mềm vẫn chạy ở khay hệ thống để gửi dữ liệu còn chờ. Muốn tắt hẳn, bấm chuột phải biểu tượng ở khay rồi chọn **Thoát**.

</details>

<details>

<summary>Thêm hoặc sửa người xong, tôi phải bấm gì để đồng bộ xuống máy?</summary>

Không cần bấm gì. Lưu xong là phần mềm tự gửi xuống cả hai máy, rồi đọc lại để kiểm tra. Cột **Có trên 2 máy** của người đó chuyển sang **Đủ 2 máy** khi xong. Chỉ cần chạy So khớp sau khi lấy dữ liệu từ máy, thêm máy, khôi phục bản sao lưu hoặc có người thêm thẳng trên máy. Xem bảng ở [Theo dõi việc gửi xuống máy](../may-cham-cong/theo-doi-gui-xuong-may.md#ban-can-bam-gi-de-dong-bo).

</details>

<details>

<summary>Vì sao So khớp hiện cả trăm người lạ?</summary>

Vì phần mềm chưa có những người đó. So khớp so từng máy với danh sách trong phần mềm, không so hai máy với nhau. Hãy [lấy dữ liệu từ máy](../may-cham-cong/lay-du-lieu-tu-may.md) có đủ người nhất trước, rồi So khớp lại: lúc đó chỉ còn những người khác nhau giữa hai máy.

</details>

## Khuôn mặt và vân tay

<details>

<summary>Nên lấy mấy ngón tay cho mỗi người?</summary>

Nên lấy 2 ngón, mỗi tay một ngón, để khi một ngón bị thương hoặc ướt, người đó vẫn chấm công bằng ngón còn lại. Phần mềm cho chọn trên hình hai bàn tay. Xem [Lấy vân tay](../nguoi-dung/lay-van-tay.md).

</details>

<details>

<summary>Thêm người mới mà chụp mặt, quét vân tay đều lỗi, người đó có lên máy không?</summary>

Không. Phần mềm chỉ giữ và gửi người mới xuống máy khi đã có khuôn mặt hoặc ít nhất một vân tay. Rời các bước thêm người khi vẫn chưa có thì phần mềm bỏ người đó, kể cả khi tắt phần mềm giữa chừng.

</details>

<details>

<summary>Mã nhân viên gợi ý tính thế nào? Xoá người có mã cuối thì sao?</summary>

Từ bản 0.9.10, phần mềm gợi ý số lớn nhất đang dùng cộng 1. Xoá người có mã cuối (ví dụ 173) thì khi máy gỡ xong, mã đó được gợi ý lại. Bản 0.9.9 trở về trước không bao giờ gợi ý lại mã đã xoá, nên có lúc nhảy số. Bạn luôn gõ được mã khác khi thêm người. Để rà mã theo thứ tự, chọn **Sắp xếp → Mã NV tăng dần** ở danh sách.

</details>

<details>

<summary>Sao không xem được hình vân tay như ảnh khuôn mặt?</summary>

Máy chấm công không lưu hình chụp ngón tay. Khi quét, máy chuyển ngón tay thành một chuỗi dữ liệu đã mã hoá rồi chỉ gửi chuỗi đó, kèm số ngón và chất lượng. Phần mềm vì vậy chỉ cho biết ngón nào đã lấy và chất lượng bao nhiêu: bấm số ngón ở cột **Vân tay** trong danh sách để xem.

</details>

<details>

<summary>Danh sách công ty lấy từ đâu? Sao có người chưa có nhóm?</summary>

Từ tên người dùng. Từ bản 0.9.6, tên có dạng mã công ty và dấu chấm ở đầu (ví dụ `ABC. Lan`) được xếp vào công ty đó, cả người đã có trong phần mềm lẫn người lấy về sau này. Người có tên không theo dạng này thì chưa có nhóm: mở người đó, bấm **Sửa** và chọn công ty. Nhóm bạn đã chọn hoặc bỏ bằng tay thì phần mềm không tự đổi lại.

</details>

<details>

<summary>Vân tay đã lấy ở máy này, sao máy kia không nhận?</summary>

Vân tay chỉ dùng chung giữa các máy có cùng loại cảm biến. Nếu hai máy khác loại, phần mềm báo cần quét lại ngón đó trực tiếp ở máy kia. Xem [Những trường hợp không làm được](gioi-han.md).

</details>

## Dùng cùng công cụ khác

<details>

<summary>Một người chỉ được vào một tầng thì làm sao?</summary>

Phần mềm gửi mỗi người xuống tất cả các máy. Người chỉ được vào một tầng thì không thêm vào phần mềm: đăng ký trực tiếp trên máy đó hoặc bằng iVMS-4200. Khi So khớp, họ hiện là người lạ; để **Bỏ qua**, đừng chọn **Nhập vào app**.

</details>

<details>

<summary>Tôi vẫn dùng iVMS-4200 hoặc menu của máy để thêm người được không?</summary>

Được, nhưng phần mềm không biết cho tới khi bạn chạy [So khớp 2 máy](../may-cham-cong/so-khop-2-may.md). Khi đó những người này hiện là người lạ và bạn chọn **Nhập vào app**, **Gỡ khỏi máy** hoặc **Bỏ qua**. Để tránh lệch, nên chỉ dùng một công cụ để thêm người.

</details>

<details>

<summary>Máy chấm công bị reset hoặc tôi thay máy mới thì làm sao?</summary>

Thêm máy trong tab **Máy chấm công & cập nhật** (nếu là máy mới), rồi chạy **So khớp 2 máy**. Phần mềm gửi lại toàn bộ người, khuôn mặt và vân tay từ bản gốc, không phải chụp lại.

</details>

## Dữ liệu và sao lưu

<details>

<summary>Gỡ cài đặt phần mềm có xoá dữ liệu không?</summary>

Không. Dữ liệu nằm ở `%LOCALAPPDATA%\AccessControlSystem`, tách khỏi thư mục cài. Gỡ hoặc cập nhật không đụng tới thư mục này. Muốn xoá hết, bạn tự xoá thư mục sau khi gỡ. Xem [Cài đặt](../bat-dau/cai-dat.md).

</details>

<details>

<summary>Lỡ tay xoá hẳn một người thì lấy lại được không?</summary>

Được. Người đã xoá nằm trong **Thùng rác** (nút ở tab **Người dùng**, cạnh các bộ lọc) cùng ảnh mặt và vân tay. Bấm **Khôi phục**: phần mềm đưa người đó lại vào danh sách và gửi lại xuống cả hai máy. Thùng rác giữ tới khi bạn bấm **Xoá vĩnh viễn** hoặc **Dọn sạch thùng rác**. Xem [Xoá hẳn một người](../nguoi-dung/xoa-han.md).

</details>

<details>

<summary>Tôi quên mật khẩu sao lưu thì làm sao?</summary>

Không có cách lấy lại, kể cả người phát triển. Các bản sao lưu đã tạo bằng mật khẩu đó không mở được. Bạn vẫn dùng bình thường vì dữ liệu hiện tại không phụ thuộc vào mật khẩu này. Vào tab **Máy chấm công & cập nhật**, mục **Sao lưu**, bấm **Đổi mật khẩu** để đặt mật khẩu mới cho các bản sao lưu sau này. Các bản sao lưu cũ vẫn cần mật khẩu cũ để mở. Hãy ghi mật khẩu ở nơi an toàn.

</details>

<details>

<summary>Máy tính hỏng hoặc phải cài lại Windows thì dữ liệu còn không?</summary>

Còn, nếu bạn có file sao lưu `.acsbak` và nhớ **mật khẩu sao lưu**. Nên chép thêm một bản ra USB hoặc ổ mạng. Cài phần mềm lại, rồi khôi phục. Hai máy chấm công không mất gì. Xem [Sao lưu và khôi phục](../may-cham-cong/sao-luu-khoi-phuc.md).

</details>

<details>

<summary>Phần mềm có gửi dữ liệu nhân viên ra Internet không?</summary>

Không. Phần mềm chỉ kết nối tới hai máy chấm công trong mạng nội bộ và tới GitHub để kiểm tra và tải bản cập nhật, không gửi dữ liệu về người hay máy chấm công. Xem [Dữ liệu cá nhân và quyền riêng tư](du-lieu-ca-nhan.md).

</details>

<details>

<summary>Phần mềm đã dùng được trên máy chấm công thật chưa?</summary>

Phần mềm đã chạy trên hai máy DS-K1T9342MFWX thật (phần mềm máy V4.39): kết nối, lấy dữ liệu 157 người kèm khuôn mặt và vân tay, So khớp, hình camera trực tiếp (cả lúc máy đang chụp). Máy nhận lệnh chụp khuôn mặt và quét vân tay qua mạng; một lần đăng ký trọn vẹn với người thật (chụp được mặt, quét được ngón) chưa được ghi nhận. Nếu bạn dùng model khác, hãy chạy Probe trước: [Kiểm tra máy thật bằng Probe](probe.md).

</details>
