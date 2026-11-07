---
description: Tra cứu thông báo lỗi hoặc triệu chứng thường gặp, nguyên nhân và cách khắc phục.
---

# Xử lý sự cố

Tìm thông báo bạn thấy trên màn hình trong danh sách dưới đây. Các lỗi gửi xuống máy hiện ở trang của từng người (tab **Người dùng**, mở người có dấu **Lỗi gửi, mở để xem**), kèm nút **Thử lại ngay** và mục **Chi tiết**. Mọi thao tác và lỗi cũng được ghi trong **Nhật ký**.

## Kết nối và đăng nhập máy

<details>

<summary>Máy báo sai tài khoản hoặc mật khẩu, hoặc đã khoá tạm thời</summary>

Thông báo đầy đủ: **Sai mật khẩu hoặc máy đang khoá tạm (khoảng 30 phút). Kiểm tra mật khẩu ở tab "Máy chấm công & cập nhật" (nút Sửa mật khẩu).** Trên máy hiện chấm **Sai mật khẩu, đã dừng**, và phần mềm báo ở khay hệ thống.

**Nguyên nhân:** mật khẩu của máy đã đổi, hoặc nhập sai. Máy Hikvision khoá địa chỉ IP khoảng 30 phút sau vài lần đăng nhập sai, nên phần mềm dừng ngay sau lần sai đầu và không tự thử lại.

**Cách khắc phục:**

1. Mở tab **Máy chấm công & cập nhật**.
2. Ở máy bị lỗi, bấm **Sửa mật khẩu**, nhập mật khẩu đúng của máy rồi bấm **Lưu máy**.
3. Bấm **Kiểm tra kết nối**. Nếu vẫn lỗi, máy có thể đang khoá địa chỉ IP: chờ khoảng 30 phút rồi kiểm tra lại.

</details>

<details>

<summary>Mật khẩu đúng, máy báo đã kết nối rồi khoảng 30 giây sau báo Sai mật khẩu, đã dừng</summary>

**Nguyên nhân:** lỗi của bản 0.9.0. Máy Hikvision chỉ giữ phiên đăng nhập vài giây, bản cũ hiểu nhầm phiên hết hạn là sai mật khẩu. Máy không tính đó là lần đăng nhập sai.

**Cách khắc phục:** cập nhật lên bản mới nhất (xem [Cập nhật phần mềm](../may-cham-cong/cap-nhat-phan-mem.md)). Sau đó ở từng máy, bấm **Sửa mật khẩu**, nhập lại mật khẩu rồi bấm **Lưu máy**.

</details>

<details>

<summary>Máy không trả lời kịp / Không kết nối được máy / Mất kết nối với máy giữa chừng</summary>

Trên máy hiện chấm **Mất kết nối**.

**Nguyên nhân:** máy tắt, rút dây mạng, sai địa chỉ IP, hoặc máy tính và máy chấm công không cùng mạng.

**Cách khắc phục:**

1. Kiểm tra nguồn và dây mạng của máy chấm công.
2. Kiểm tra địa chỉ IP ở tab **Máy chấm công & cập nhật** có đúng không (bấm **Sửa**).
3. Không cần làm gì thêm với việc đang chờ: việc nằm trong hàng đợi (**Đang chờ — tự gửi khi máy có mạng.**). Phần mềm tự thử lại và báo ở khay hệ thống khi máy kết nối lại.

</details>

<details>

<summary>Máy đang bận hoặc gặp lỗi tạm thời</summary>

**Nguyên nhân:** máy đang xử lý việc khác.

**Cách khắc phục:** chờ một lúc, phần mềm tự thử lại. Nếu chụp hoặc quét báo **Máy đang chụp/quét, đợi xong rồi bấm lại**, đợi người trước xong rồi bấm lại.

</details>

## Lấy dữ liệu và So khớp

<details>

<summary>So khớp báo nhiều người thiếu vân tay, mỗi lần chạy ra một danh sách khác</summary>

**Nguyên nhân:** máy Hikvision chỉ đọc được vân tay cho một nơi hỏi tại một thời điểm. Bản trước 0.9.3 có thể bị cắt ngang khi đọc, nên báo thiếu cả những ngón máy đang có. Một chương trình khác (iVMS-4200, công cụ cũ) đọc vân tay của máy cùng lúc cũng gây ra chuyện này.

**Cách khắc phục:**

1. **Không** bấm **Bổ sung** khi danh sách thiếu vân tay trông bất thường.
2. Cập nhật lên bản mới nhất (xem [Cập nhật phần mềm](../may-cham-cong/cap-nhat-phan-mem.md)).
3. Đóng các chương trình khác đang kết nối tới máy chấm công, rồi bấm **Đọc lại**.

</details>

<details>

<summary>Đang lấy dữ liệu, chuyển sang tab khác thì như bị dừng</summary>

Từ bản 0.9.3, việc lấy vẫn chạy tiếp khi chuyển tab: tab **Người dùng** hiện tiến độ, và quay lại mục **Lấy dữ liệu từ máy** vẫn thấy thanh tiến độ. Ở bản cũ, việc lấy cũng chạy tiếp nhưng màn hình không hiện; đợi khoảng 1 phút rồi xem danh sách người. Xem [Lấy dữ liệu từ máy](../may-cham-cong/lay-du-lieu-tu-may.md).

</details>

## Gửi xuống máy

<details>

<summary>Máy đang có người khác dùng mã này</summary>

Thông báo đầy đủ: **Máy đang có người khác dùng mã này (tên người trên máy). Đổi mã hoặc xử lý bằng So khớp.**

**Nguyên nhân:** máy đã có một người khác mang cùng mã nhân viên, thường do thêm thẳng trên máy hoặc bằng iVMS-4200.

**Cách khắc phục:** nếu đó là cùng một người, chạy [So khớp 2 máy](../may-cham-cong/so-khop-2-may.md) và chọn **Nhập vào app** ở mục người lạ. Nếu là hai người khác nhau, đổi mã nhân viên của người đang thêm. Phần mềm không tự ghi đè người trên máy.

</details>

<details>

<summary>Mã này vừa xoá và đang chờ gỡ khỏi máy. Đợi máy gỡ xong hoặc dùng mã khác.</summary>

**Nguyên nhân:** bạn vừa **Xoá hẳn** một người rồi thêm lại đúng mã đó ngay, trong khi lệnh gỡ khỏi máy vẫn còn trong hàng đợi (thường do máy đang mất kết nối).

**Cách khắc phục:** chờ máy online để lệnh gỡ hoàn tất rồi thêm lại, hoặc dùng mã khác. Xem [Theo dõi việc gửi xuống máy](../may-cham-cong/theo-doi-gui-xuong-may.md).

</details>

<details>

<summary>Máy đã đầy số người tối đa / Thư viện khuôn mặt trên máy đã đầy / Máy đã đầy bộ nhớ vân tay</summary>

**Nguyên nhân:** mỗi model máy có giới hạn số người, số khuôn mặt và số vân tay.

**Cách khắc phục:** xoá bớt người, khuôn mặt hoặc vân tay không còn dùng trên máy (nên khoá rồi xoá hẳn người đã nghỉ việc), rồi bấm **Thử lại ngay**.

</details>

<details>

<summary>Máy báo đã nhận nhưng đọc lại không thấy</summary>

Thông báo đầy đủ: **Máy báo đã nhận nhưng đọc lại không thấy. Bấm Thử lại; nếu vẫn lỗi, xem Chi tiết.**

**Nguyên nhân:** sau khi gửi, phần mềm đọc lại từ máy để kiểm tra và không thấy dữ liệu.

**Cách khắc phục:** bấm **Thử lại ngay**. Nếu vẫn lỗi, bấm **Chi tiết** và gửi nội dung cho người hỗ trợ.

</details>

<details>

<summary>Máy này không hỗ trợ chức năng đó</summary>

Các dạng thông báo: **Máy này không hỗ trợ chụp khuôn mặt**, **Máy này không hỗ trợ quét vân tay**.

**Nguyên nhân:** máy cũ hoặc firmware cũ không nhận lệnh này qua mạng.

**Cách khắc phục:** kiểm tra model máy hoặc cập nhật firmware. Với khuôn mặt, dùng webcam hoặc ảnh có sẵn.

</details>

## Khuôn mặt

<details>

<summary>Ảnh khuôn mặt chưa đạt chất lượng / Máy không nhận ra khuôn mặt trong ảnh</summary>

**Nguyên nhân:** ảnh mờ, ngược sáng, mặt nhỏ hoặc nghiêng.

**Cách khắc phục:** chụp lại ảnh rõ mặt, đủ sáng, nhìn thẳng. Xem [Chụp khuôn mặt](../nguoi-dung/chup-khuon-mat.md).

</details>

<details>

<summary>Ảnh khuôn mặt phải là tệp JPEG / Ảnh khuôn mặt quá lớn (tối đa 200 KB)</summary>

**Nguyên nhân:** ảnh chọn từ máy tính không phải JPEG hoặc quá dung lượng.

**Cách khắc phục:** chọn ảnh JPEG khác, hoặc chụp bằng webcam.

</details>

<details>

<summary>Máy chưa chụp được. Mời đứng thẳng trước camera rồi bấm lại.</summary>

**Cách khắc phục:** người cần chụp đứng thẳng trước camera của máy, rồi bấm lại nút chụp.

</details>

## Vân tay

<details>

<summary>Vân tay chưa rõ (chất lượng …)</summary>

Thông báo đầy đủ: **Vân tay chưa rõ (chất lượng …). Đặt ngón ngay giữa, giữ yên rồi quét lại.**

**Nguyên nhân:** vân tay quét không đủ chất lượng (ngón ướt, bẩn, đặt lệch).

**Cách khắc phục:** lau tay, đặt ngón ngay giữa cảm biến, giữ yên rồi quét lại. Xem [Lấy vân tay](../nguoi-dung/lay-van-tay.md).

</details>

<details>

<summary>Vân tay này không hợp với cảm biến của máy</summary>

Thông báo đầy đủ: **Vân tay này không hợp với cảm biến của máy. Quét lại ngón này trực tiếp ở máy đó.** Trên trang người, dạng tương tự là **Cần quét** tên ngón **trực tiếp ở** tên máy.

**Nguyên nhân:** hai máy không cùng loại cảm biến, nên mẫu vân tay của máy này không dùng được ở máy kia.

**Cách khắc phục:** quét lại đúng ngón đó trực tiếp trên máy được nêu. Xem [Những trường hợp không làm được](gioi-han.md).

</details>

<details>

<summary>Máy chưa quét được vân tay / Máy chưa sẵn sàng nhận vân tay</summary>

**Cách khắc phục:** đặt ngón tay lên cảm biến, giữ yên rồi bấm lại; hoặc chờ một lát rồi bấm **Thử lại**.

</details>

## Sao lưu, khôi phục và cập nhật

<details>

<summary>Mật khẩu sao lưu không đúng hoặc file đã hỏng</summary>

**Nguyên nhân:** nhập sai **mật khẩu sao lưu**, hoặc chọn nhầm file không phải `.acsbak` của phần mềm. Dữ liệu hiện tại không bị đổi.

**Cách khắc phục:** nhập lại mật khẩu đã đặt khi tạo bản sao lưu đó. Nếu bạn đã quên mật khẩu, không có cách lấy lại: bản sao lưu đó không mở được. Thử bản sao lưu khác. Xem [Sao lưu và khôi phục](../may-cham-cong/sao-luu-khoi-phuc.md).

</details>

<details>

<summary>Không khôi phục được bản sao lưu. Dữ liệu cũ vẫn được giữ nguyên.</summary>

**Cách khắc phục:** thử lại với bản sao lưu khác. Nếu còn lỗi, gửi dòng lỗi trong **Nhật ký** cho người hỗ trợ. Dữ liệu cũ không bị mất.

</details>

<details>

<summary>Hãy nhập đường dẫn đầy đủ / Không thấy thư mục này</summary>

**Nguyên nhân:** khi chọn thư mục sao lưu, đường dẫn chưa đầy đủ hoặc USB, ổ mạng chưa cắm.

**Cách khắc phục:** nhập đường dẫn đầy đủ, ví dụ `E:\SaoLuuACS` hoặc `\\may-chu\SaoLuu`, kiểm tra USB hoặc ổ mạng, rồi bấm **Xem**.

</details>

<details>

<summary>Không tải được bản mới / Bản mới tải về bị hỏng hoặc không đúng</summary>

Các thông báo: **Không kiểm tra được bản mới**, **Không tải được bản mới**, **Bản mới tải về không đủ, hãy thử lại.**, **Bản mới tải về bị hỏng hoặc không đúng, phần mềm đã bỏ nó đi.**

**Nguyên nhân:** mất Internet, hoặc file tải về không đủ dung lượng hay không trùng mã kiểm tra.

**Cách khắc phục:** kiểm tra Internet rồi bấm **Kiểm tra bản mới** và **Cập nhật ngay** lại. Nếu vẫn lỗi, tải bộ cài mới ở trang Releases và chạy trực tiếp; dữ liệu được giữ nguyên. Xem [Cập nhật phần mềm](../may-cham-cong/cap-nhat-phan-mem.md).

</details>

<details>

<summary>Không chạy được bộ cài cập nhật</summary>

Thông báo đầy đủ: **Không chạy được bộ cài cập nhật. Hãy thử lại hoặc tải bản mới trên trang phát hành.** Phần mềm vẫn chạy bình thường.

**Cách khắc phục:** thử lại; nếu không được, tải bộ cài ở trang Releases và chạy tay.

</details>

## Phần mềm

<details>

<summary>Không khởi động được phần mềm</summary>

Thông báo: **Không khởi động được phần mềm. Hãy đóng mọi cửa sổ Access Control System rồi mở lại.**

**Cách khắc phục:** thoát hẳn phần mềm ở khay hệ thống (chuột phải > **Thoát**) rồi mở lại. Nếu còn lỗi, mở Task Manager, tắt tiến trình **AccessControlSystem**, rồi mở lại.

</details>

<details>

<summary>Dữ liệu đang lỗi, hãy khởi động lại phần mềm</summary>

**Cách khắc phục:** thoát hẳn rồi mở lại. Nếu vẫn lỗi, khôi phục từ bản sao lưu gần nhất.

</details>

<details>

<summary>Có lỗi không lường trước. Xem Nhật ký.</summary>

**Cách khắc phục:** mở **Nhật ký**, tìm dòng lỗi mới nhất rồi khởi động lại phần mềm. Nếu lặp lại, gửi nội dung dòng đó cho người hỗ trợ.

</details>

## Gửi báo lỗi

Nếu các cách trên không giải quyết được, tạo issue mới trên [trang Issues của dự án trên GitHub](https://github.com/tdphuong97/AccessControlSystem/issues) kèm các thông tin: phiên bản phần mềm (xem ở tab **Máy chấm công & cập nhật**), thông báo lỗi nguyên văn, nội dung ở **Chi tiết** hoặc dòng **Nhật ký** liên quan. Không gửi mật khẩu.
