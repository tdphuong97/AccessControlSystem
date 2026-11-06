# Access Control System

Phần mềm Windows để **đăng ký khuôn mặt và vân tay** cho hai máy kiểm soát ra vào Hikvision. Nhập một lần, phần mềm tự gửi xuống cả hai máy, kiểm tra lại, và giữ bản gốc để gửi lại khi máy bị reset hoặc thay mới.

Mã nguồn công khai, giấy phép MIT. Phiên bản hiện tại là **0.9.0**: đã kiểm thử bằng máy giả lập, **chưa thử trên máy chấm công thật** (xem mục [Probe](#probe-kiểm-tra-máy-thật-tại-chỗ)).

## Cài đặt

1. Vào trang **Releases** của dự án trên GitHub, tải `AccessControlSystem-Setup-<phiên bản>.exe`.
2. Chạy file vừa tải. Lần đầu Windows có thể hiện cảnh báo xanh **"Windows đã bảo vệ máy tính của bạn"**, vì bộ cài chưa có chữ ký số (chứng chỉ ký số tốn phí hằng năm). Bấm **Thông tin thêm**, rồi bấm **Vẫn chạy**. Chỉ phải làm một lần; các lần cập nhật sau không hỏi lại.
3. Làm theo hướng dẫn. Bộ cài không cần quyền quản trị. Có hai lựa chọn:
   - **Tạo biểu tượng ngoài màn hình**;
   - **Chạy cùng Windows**: nên chọn, để phần mềm tự gửi dữ liệu còn chờ khi máy tính vừa bật.
4. Bấm **Mở Access Control System ngay**.

Phần mềm chạy trên máy đã cài, không mở cổng mạng, nên không dùng được từ điện thoại hay máy khác (cố ý, để an toàn).

## Lần đầu mở: thiết lập

Một trình hướng dẫn 5 bước, mất khoảng 5 phút:

1. **Thêm máy**: đặt tên (ví dụ `Máy tầng trệt`, `Máy tầng 1`), nhập địa chỉ IP (ví dụ `192.168.1.201`), tài khoản và mật khẩu của máy. Đánh dấu máy nào dùng để chụp mặt, máy nào dùng để quét vân tay.
2. **Kiểm tra**: phần mềm đọc model, số người và cho biết máy có chụp mặt, quét vân tay qua mạng được không.
3. **Lấy dữ liệu**: đọc người, ảnh mặt và vân tay đang có trên máy về phần mềm. Có thanh tiến độ, chạy lại được nếu bị đứt giữa chừng.
4. **Sao lưu**: đặt **mật khẩu sao lưu** (xem [Sao lưu và khôi phục](#sao-lưu-và-khôi-phục)).
5. **So khớp**: đối chiếu hai máy với bản gốc trong phần mềm.

## Thêm một người (4 bước)

Bấm **Thêm người mới** ở tab **Người dùng**:

1. **Thông tin**: họ tên, mã nhân viên, nhóm, ngày hết hiệu lực (có thể để trống).
2. **Khuôn mặt**: chụp bằng camera của máy chấm công (cách chính), bằng webcam máy tính, hoặc chọn ảnh có sẵn. Phần mềm tự cắt và nén ảnh theo yêu cầu của máy.
3. **Vân tay**: chọn ngón trên hình hai bàn tay rồi đặt ngón lên máy quét. Phần mềm báo chất lượng ngay; chất lượng thấp thì quét lại. Nên lấy 2 ngón, mỗi tay 1 ngón.
4. **Xong**: xem người đó đã tới từng máy chưa. Máy mất mạng thì việc nằm trong hàng đợi và tự gửi khi máy online lại.

## So khớp

Dùng khi nghi hai máy lệch nhau, sau khi máy bị reset hoặc thay máy mới, hoặc có người sửa thẳng trên máy.

- Phần mềm đối chiếu từng máy với bản gốc và nói bằng lời dễ hiểu, ví dụ "Máy tầng 1: thiếu 3 người, 2 người thiếu vân tay".
- Mặc định chọn sẵn **Bổ sung** phần thiếu. Bấm **Bổ sung** để gửi.
- **Người lạ** (có trên máy, không có trong phần mềm): chọn **Nhập vào app**, **Gỡ khỏi máy** hoặc **Bỏ qua** (mặc định là Bỏ qua).
- Phần mềm **không bao giờ tự xoá**; mọi lần gỡ đều do bạn chọn.

## Khoá, mở lại, xoá người

- **Khoá ra vào** (người nghỉ việc): gỡ người khỏi các máy, dữ liệu vẫn giữ trong phần mềm. Nếu phần mềm chưa có đủ ảnh mặt và vân tay của người đó thì **không gỡ**, chỉ tắt hiệu lực trên máy, để không làm mất bản sinh trắc duy nhất.
- **Mở lại**: gửi lại đủ người, mặt và vân tay từ bản đã lưu, không phải chụp lại.
- **Xoá hẳn**: gỡ khỏi máy và xoá dữ liệu trong phần mềm, có hỏi xác nhận. Nên xoá hẳn người đã nghỉ việc quá 6 tháng (xem [PRIVACY.md](PRIVACY.md)).

## Sao lưu và khôi phục

- Phần mềm tự sao lưu mỗi ngày và trước mỗi lần cập nhật, giữ 14 bản. Vào tab **Máy chấm công & cập nhật**, mục **Sao lưu** để bấm **Sao lưu ngay** hoặc **Khôi phục**.
- File sao lưu được mã hoá bằng **mật khẩu sao lưu** bạn đặt ở lần chạy đầu.
- **Quan trọng: mất mật khẩu sao lưu thì không mở được bản sao lưu, không ai lấy lại được.** Hãy ghi lại ở nơi an toàn.
- Nên chép thêm một bản ra USB hoặc ổ mạng định kỳ. Khi máy tính hỏng hoặc cài lại Windows, cài phần mềm lại, rồi **Khôi phục** từ file sao lưu cùng mật khẩu sao lưu (khoá mã hoá trong máy cũ gắn với tài khoản Windows cũ nên không dùng lại được).
- Dữ liệu nằm trong `%LOCALAPPDATA%\AccessControlSystem`, tách khỏi thư mục cài, nên cập nhật hoặc gỡ cài đặt không làm mất dữ liệu.

## Cập nhật

Phần mềm kiểm tra bản mới trên GitHub 1 phút sau khi mở, rồi mỗi 20 giờ (tắt được ở tab **Máy chấm công & cập nhật**). Có bản mới thì hiện thanh trên màn hình chính; bấm **Cập nhật ngay**. Phần mềm tải về, kiểm tra kích thước và SHA-256, tự sao lưu (nếu đã đặt mật khẩu sao lưu), cài im lặng rồi mở lại. Dữ liệu được giữ nguyên.

## Probe: kiểm tra máy thật tại chỗ

Vì bản 0.9.0 chưa thử trên máy thật, có kèm `AccessControlSystem-Probe.exe` (tải ở trang Releases). Công cụ chạy một lần tại chỗ, kiểm tra các lệnh phần mềm sẽ dùng, rồi tạo file báo cáo để gửi lại cho người phát triển điều chỉnh.

1. Cho máy tính và máy chấm công cùng mạng. Mở PowerShell hoặc Command Prompt tại thư mục có file Probe.
2. Chạy (thay địa chỉ và tài khoản bằng của máy thật):

   ```
   AccessControlSystem-Probe.exe --host http://192.168.1.201 --user admin --pass ******
   ```

   Có máy thứ hai thì thêm `--peer http://192.168.1.202 --peer-user admin --peer-pass ******`. Thêm `--out THƯ_MỤC` nếu muốn chọn nơi lưu báo cáo.
3. Làm theo lời nhắc trên màn hình (ví dụ đặt ngón tay lên máy quét, rồi bấm Enter).
4. Probe chỉ tạo, sửa và xoá duy nhất một người thử mã `99999` (TEST ACS) và tự xoá khi xong, kể cả khi bấm Ctrl+C. Người thật trên máy không bị đụng tới.
5. Kết quả nằm trong file `probe-report-yyyyMMdd-HHmm.txt`; mật khẩu đã được che bằng `***`. Gửi file này lại cho người hỗ trợ.

Khi Probe đạt, phiên bản sẽ lên 1.0.

## Những trường hợp không làm được

| Trường hợp | Vì sao | Phần mềm làm gì, bạn cần làm gì |
|---|---|---|
| Vân tay không chép sang máy kia được | Vân tay chỉ dùng chung giữa các máy có cùng loại cảm biến | Báo rõ máy nào không nhận; quét ngón đó trực tiếp ở máy kia. Nên kiểm tra model của hai máy trước |
| Người cũ không tải được ảnh mặt về | Một số máy chỉ giữ mẫu nhận diện, không cho tải ảnh gốc | Đánh dấu "cần chụp lại"; người đó vẫn vào được máy đang có mặt |
| Máy từ chối ảnh mặt | Ảnh mờ, ngược sáng, mặt nhỏ hoặc nghiêng, quá dung lượng | Phần mềm nén và cắt ảnh; vẫn bị từ chối thì báo "chụp lại" kèm lý do |
| Máy cũ không nhận lệnh chụp mặt hoặc quét vân tay qua mạng | Tuỳ model và firmware | Dùng webcam cho mặt; vân tay thì đăng ký trên menu máy rồi bấm "Đọc lại từ máy" |
| Máy tính tắt hoặc phần mềm không chạy | Hàng đợi chỉ gửi khi phần mềm chạy | Phần mềm chạy nền ở khay và khởi động cùng Windows; việc chờ vẫn còn và gửi tiếp khi mở lại |
| Máy chấm công bị reset hoặc thay máy mới | Máy mất hết dữ liệu | Chạy So khớp: phần mềm gửi lại toàn bộ từ bản gốc |
| Có người thêm hoặc sửa thẳng trên máy hoặc bằng iVMS-4200 | Phần mềm không biết cho tới khi So khớp | So khớp hiện "người lạ" để nhập hoặc gỡ. Nên chỉ dùng một công cụ |
| Sai mật khẩu máy | Hikvision khoá địa chỉ IP khoảng 30 phút sau vài lần sai | Phần mềm dừng ngay sau lần sai đầu, không tự thử lại. Sửa mật khẩu rồi thử lại |
| Hỏng máy tính hoặc cài lại Windows | Khoá mã hoá gắn với tài khoản Windows cũ | Khôi phục từ file sao lưu cùng mật khẩu sao lưu |
| Cảnh báo "Windows đã bảo vệ máy tính" khi cài lần đầu | Bộ cài chưa ký số | Bấm **Thông tin thêm → Vẫn chạy** một lần |
| Dùng từ điện thoại hoặc máy khác | Bản đầu chỉ chạy trên máy đã cài | Không hỗ trợ (cố ý, để không mở cổng mạng) |
| Pháp lý về dữ liệu sinh trắc | Ảnh mặt và vân tay là dữ liệu cá nhân nhạy cảm | Xem [PRIVACY.md](PRIVACY.md) |
| Máy đầy bộ nhớ | Mỗi model có giới hạn số người, mặt, vân tay | Phần mềm báo "máy đầy" kèm số lượng đang có |

## Tài liệu khác

- [PRIVACY.md](PRIVACY.md): dữ liệu được lưu và dùng thế nào.
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md): thành phần bên thứ ba.
- [CHANGELOG.md](CHANGELOG.md): lịch sử thay đổi.

## Xây dựng từ mã nguồn

Cần .NET 10 SDK, Node.js và Inno Setup 6. Chạy `.\build.ps1`; bộ cài và Probe nằm trong `dist\`. Khi chạy thử, đặt biến môi trường `ACS_DATA` trỏ tới một thư mục tạm để không đụng dữ liệu thật.
