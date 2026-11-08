## 0.9.8

- Hình trực tiếp khi chụp khuôn mặt có vòng canh mặt như trên màn hình máy chấm công: nét đứt khi canh mặt, chuyển xanh và nhấp nháy nhẹ trong lúc máy chụp; vùng ngoài vòng tối đi. Dòng "Mời nhìn thẳng vào camera" lên phía trên khung để thanh nút ở đáy không che.

## 0.9.7

- Thêm người mới: chỉ gửi người đó xuống máy khi đã có khuôn mặt hoặc một vân tay (bước 1 chỉ lưu trong app). Đăng ký bỏ dở hay bị lỗi không còn để lại người không có gì trên máy; danh sách ghi "Chưa gửi · cần mặt hoặc vân tay". So khớp cũng không đẩy người như vậy xuống máy.
- Hình trực tiếp vẫn chạy trong lúc máy chụp mặt: lúc chụp, hình lấy qua kết nối riêng, nên không phải chờ lệnh chụp (đã thử trên máy thật: khoảng 4 hình/giây suốt lúc chụp). Xem hình trực tiếp không bao giờ tự dừng máy vì sai mật khẩu.
- Mới: Chẩn đoán máy (kết nối và đăng nhập, chụp mặt và quét vân tay qua mạng, camera, việc gửi, lần chụp/quét lỗi gần nhất, kèm cách xử lý), trên thẻ máy và trong thông báo lỗi lúc chụp, quét. Lỗi chụp mặt, quét vân tay được ghi vào Nhật ký kèm câu trả lời của máy; máy chờ 15 giây không thấy ngón tay hoặc khuôn mặt thì báo đúng như vậy.
- Nút Tiếp / Lưu luôn ở đáy cửa sổ (form thêm người, form máy chấm công, các hộp sửa): không phải cuộn mới bấm được.

## 0.9.6

- Mới: chụp khuôn mặt bằng camera máy chấm công có khung hình trực tiếp (khoảng 3 hình/giây) để canh mặt trước khi chụp. Hình lấy từ camera của chính máy, không ảnh hưởng việc chụp: lúc máy đang chụp thì app tạm dừng lấy hình.
- Mới: bấm "Có" ở cột Khuôn mặt hoặc số ngón ở cột Vân tay trong danh sách để xem nhanh ảnh mặt và các ngón đã lấy (kèm chất lượng). Vân tay không có ảnh: máy chỉ gửi dữ liệu đã mã hoá.
- Mới: nhóm / công ty lấy từ mã đầu tên ("ABC. Lan" → ABC) khi lấy dữ liệu từ máy; một lần khi khởi động cho người đã lấy trước đây (không đổi nhóm đã chọn bằng tay). Ô Nhóm / công ty thành danh sách chọn có "+ Nhóm mới…"; danh sách người có ô lọc Công ty.
- Cả giao diện to nhỏ theo cửa sổ (100% ở cỡ mặc định, từ 80% đến 150%).

## 0.9.5

- Mới: nút Hướng dẫn ở góc trên bên phải (phím F1) mở hướng dẫn sử dụng ngay trong phần mềm, đúng trang của màn hình đang dùng. Hướng dẫn đi kèm phần mềm, không cần mạng.
- Hướng dẫn bổ sung trang Lấy dữ liệu từ máy và bảng "Bạn cần bấm gì để đồng bộ".
- Bỏ khoảng trống thừa phía trên tiêu đề các màn hình.

## 0.9.4

- Danh sách người dùng chia trang: chọn 20, 50, 100 hoặc Tất cả người mỗi trang (mặc định 50), có nút Trước / Sau. Phần mềm nhớ lựa chọn trên máy tính này.

## 0.9.3

- Sửa lỗi: So khớp báo nhầm "thiếu vân tay" cho người mà máy vẫn có đủ ngón, mỗi lần chạy ra một danh sách khác. Máy Hikvision chỉ có một con trỏ đọc vân tay, lần đọc bị chen ngang thì chỉ trả về một phần. App nay đọc trọn từng người một mạch, mỗi lần một phiên mới, và không bao giờ báo thiếu khi số ngón đọc được ít hơn số máy báo.
- Lấy dữ liệu từ máy vẫn chạy khi chuyển trang: quay lại vẫn thấy tiến độ, tab Người dùng hiện dòng đang lấy dữ liệu, lấy xong danh sách tự cập nhật.
- Không chạy hai lần lấy dữ liệu cùng lúc. Mở So khớp lần nữa khi app đang đọc máy thì dùng chung lần đọc đó. So khớp chờ lấy dữ liệu xong rồi mới so.
- Đã thử trên hai máy thật: So khớp chạy hai lần liền cho cùng kết quả.

## 0.9.2

- Mới: mục "Lấy dữ liệu từ máy" trên tab Máy chấm công & cập nhật. Lấy toàn bộ người, khuôn mặt và vân tay từ một máy về app bất cứ lúc nào, không chỉ ở lần thiết lập đầu.
- So khớp 2 máy: khi app chưa có ai, đề nghị lấy dữ liệu từ máy trước thay vì liệt kê mọi người trên máy là người lạ. Sau đó So khớp chỉ hiện những người khác nhau giữa hai máy.
- Trạng thái máy đổi từ "Đang kết nối" thành "Đã kết nối" cho khỏi hiểu nhầm là đang thử kết nối.
- Sau khi cập nhật, app tự xoá bộ cài đã tải về (khoảng 49 MB mỗi lần).
- Đã thử trên hai máy thật: lấy 157 người kèm khuôn mặt và vân tay trong khoảng 50 giây, So khớp chỉ ra đúng 5 người khác nhau.

## 0.9.1

- Sửa lỗi: nhập đúng mật khẩu, máy báo "Đã kết nối" rồi khoảng 30 giây sau lại báo "Sai mật khẩu, đã dừng". Máy Hikvision chỉ giữ phiên đăng nhập vài giây; phần mềm nay tự đăng nhập lại thay vì coi đó là sai mật khẩu.
- Sửa lỗi: bấm "Kiểm tra kết nối" trên máy có nhiều người báo sai mật khẩu giữa chừng.
- Mỗi máy chấm công chỉ nhận một lệnh một lúc; hai lệnh đăng nhập cùng lúc không còn làm máy từ chối một trong hai.
- Đã thử trên hai máy DS-K1T9342MFWX thật (firmware V4.39).

## 0.9.0

- Bản đầu tiên: đăng ký khuôn mặt và vân tay cho hai máy kiểm soát ra vào Hikvision, nhập một lần và tự gửi xuống cả hai máy.
- Trình hướng dẫn lần đầu: thêm máy, kiểm tra kết nối, lấy dữ liệu có sẵn từ máy về, đặt mật khẩu sao lưu, So khớp hai máy.
- Thêm người theo 4 bước (thông tin, khuôn mặt, vân tay, xong), chụp bằng camera máy chấm công, webcam hoặc ảnh có sẵn.
- Hàng đợi gửi lên máy có tự thử lại và kiểm tra lại; máy mất mạng thì việc chờ và tự gửi khi máy online.
- So khớp: xem trước chênh lệch, bổ sung phần thiếu, xử lý người lạ; không bao giờ tự xoá.
- Khoá ra vào, mở lại, xoá hẳn người.
- Dữ liệu mã hoá trên máy tính; sao lưu tự động hằng ngày, khôi phục bằng mật khẩu sao lưu.
- Cập nhật một chạm từ GitHub Releases.
- Kèm công cụ Probe để kiểm tra máy chấm công thật tại chỗ.
- Lưu ý: chưa thử trên máy chấm công thật; ngưỡng chất lượng vân tay và một số lệnh sẽ chỉnh sau khi có báo cáo Probe.
