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

Không nên. Mỗi máy tính giữ một bản gốc riêng, không đồng bộ với nhau, nên hai bản sẽ lệch và hai máy chấm công nhận dữ liệu khác nhau. Hãy chọn một máy tính duy nhất để đăng ký nhân viên. Nếu cần chuyển sang máy tính khác, dùng [Sao lưu và khôi phục](../may-cham-cong/sao-luu-khoi-phuc.md).

</details>

<details>

<summary>Máy tính tắt thì dữ liệu có bị mất không?</summary>

Không mất. Việc chưa gửi xuống máy chấm công nằm chờ trong hàng đợi. Chúng chỉ được gửi khi phần mềm đang chạy, nên hãy chọn **Chạy cùng Windows** khi cài. Bật máy tính và mở phần mềm là việc chờ tự gửi tiếp. Xem [Theo dõi việc gửi xuống máy](../may-cham-cong/theo-doi-gui-xuong-may.md).

</details>

<details>

<summary>Tôi bấm đóng cửa sổ, phần mềm có tắt không?</summary>

Không. Phần mềm vẫn chạy ở khay hệ thống để gửi dữ liệu còn chờ. Muốn tắt hẳn, bấm chuột phải biểu tượng ở khay rồi chọn **Thoát**.

</details>

## Khuôn mặt và vân tay

<details>

<summary>Nên lấy mấy ngón tay cho mỗi người?</summary>

Nên lấy 2 ngón, mỗi tay một ngón, để khi một ngón bị thương hoặc ướt, người đó vẫn chấm công bằng ngón còn lại. Phần mềm cho chọn trên hình hai bàn tay. Xem [Lấy vân tay](../nguoi-dung/lay-van-tay.md).

</details>

<details>

<summary>Vân tay đã lấy ở máy này, sao máy kia không nhận?</summary>

Vân tay chỉ dùng chung giữa các máy có cùng loại cảm biến. Nếu hai máy khác loại, phần mềm báo cần quét lại ngón đó trực tiếp ở máy kia. Xem [Những trường hợp không làm được](gioi-han.md).

</details>

## Dùng cùng công cụ khác

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

Bản 0.9.0 đã kiểm thử bằng máy giả lập nhưng chưa thử trên máy thật. Nếu bạn là người triển khai đầu tiên, hãy chạy Probe trước: [Kiểm tra máy thật bằng Probe](probe.md).

</details>
