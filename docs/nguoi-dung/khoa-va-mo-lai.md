---
description: Khoá ra vào cho người nghỉ việc mà vẫn giữ mặt và vân tay trong phần mềm, và mở lại khi cần mà không phải chụp lại.
---

# Khoá ra vào và mở lại

**Ai làm:** người phụ trách đăng ký nhân viên · **Khi nào:** nhân viên nghỉ việc hoặc tạm ngừng ra vào

Khoá ra vào khác với xoá: phần mềm vẫn giữ ảnh mặt và vân tay trong bản gốc, nên người đó quay lại thì bạn **Mở lại** là xong, không phải chụp lại. Muốn xoá hẳn dữ liệu, xem [Xoá hẳn một người](xoa-han.md).

{% tabs %}
{% tab title="Khoá" %}
1. Mở người cần khoá từ tab **Người dùng** (xem [Tìm và xem một người](tim-va-xem.md)).
2. Bấm **Khoá ra vào**. Hộp hỏi **Khoá ra vào cho** kèm tên người đó hiện ra và nhắc rằng người này sẽ không ra vào được nữa, phần mềm vẫn giữ mặt và vân tay.
3. Bấm **Khoá ra vào** để xác nhận, hoặc **Thôi** để huỷ.

Màn hình của người đó hiện dải **Người này đã bị khoá ra vào**, nhãn đổi thành **Đã khoá ra vào**, và nút **Mở lại** thay cho **Khoá ra vào**. Trong danh sách, người đó nằm ở nút lọc **Đã khoá** và không được tính vào số người đang có quyền ra vào.
{% endtab %}

{% tab title="Mở lại" %}
1. Mở người đã khoá. Cách nhanh: ở tab **Người dùng**, bấm nút lọc **Đã khoá**.
2. Bấm **Mở lại**. Không có hộp xác nhận.
3. Thông báo **Đã mở lại. App đang gửi cho 2 máy.** hiện ra.

Phần mềm gửi lại cho từng máy: thông tin người đó (ở trạng thái được phép ra vào), ảnh khuôn mặt nếu bản gốc có, và từng ngón vân tay đã lưu. Khi các thẻ máy ghi **Đã đủ**, người đó ra vào lại được.
{% endtab %}
{% endtabs %}

## Phần mềm làm gì trên máy khi khoá

Với mỗi máy, phần mềm chọn một trong hai cách.

| Cách | Máy còn giữ người đó không | Khi nào |
| --- | --- | --- |
| Gỡ khỏi máy | Không, người đó bị xoá khỏi máy | Phần mềm chắc chắn đã có bản gốc đủ để gửi lại: ảnh mặt (nếu máy có mặt) và mọi ngón mà máy đang có. |
| Chỉ tắt hiệu lực | Có, nhưng không cho ra vào | Phần mềm chưa chắc có đủ bản gốc, hoặc chưa đọc được danh sách vân tay trên máy. |

Quy tắc này để không bao giờ làm mất bản sinh trắc học duy nhất của một người. Cụ thể:

- Máy có ảnh mặt mà phần mềm không có bản sao: chỉ tắt.
- Máy có ngón vân tay mà phần mềm không có mẫu tương ứng với loại cảm biến của máy đó: chỉ tắt. Vân tay của máy này có thể bị máy kia từ chối, nên chỉ mẫu cùng loại cảm biến mới được tính.
- Phần mềm chưa từng xác nhận máy đó có gì (ví dụ máy mới thêm, chưa chạy So khớp): chỉ tắt.
- Ngay trước khi gỡ, phần mềm còn đọc lại chính máy đó. Nếu thấy máy có sinh trắc học mà phần mềm chưa có, nó đổi sang chỉ tắt và ghi vào **Nhật ký**: **Chỉ tắt (không gỡ) vì máy có sinh trắc mà app chưa có.**
- Máy mất kết nối: việc khoá nằm trong hàng đợi và làm khi máy có mạng lại. Người đó vẫn ra vào được ở máy đó cho đến lúc ấy.

Thẻ máy của người đã khoá ghi **Đã đủ** khi người đó đã bị gỡ hoặc đã bị tắt trên máy. Nếu máy vẫn cho người đã khoá ra vào, thẻ nhắc **Máy này vẫn cho người đã khoá ra vào — chạy So khớp 2 máy.** Xem [So khớp 2 máy](../may-cham-cong/so-khop-2-may.md).

{% hint style="warning" %}
Máy tắt hoặc phần mềm không chạy thì việc khoá chưa tới máy. Với người nghỉ việc cần chặn ngay, kiểm tra thẻ **Trạng thái trên máy** đã ghi **Đã đủ** cho cả hai máy.
{% endhint %}

{% hint style="success" %}
**Kết quả:** người bị khoá không ra vào được ở cả hai máy, dữ liệu vẫn còn trong phần mềm. Mở lại thì máy nhận đủ như cũ. Người khoá quá 6 tháng được nhắc nên [xoá hẳn](xoa-han.md).
{% endhint %}

## Lỗi thường gặp

<details>

<summary>Người đã khoá vẫn ra vào được ở một máy</summary>

Máy đó có thể chưa nhận lệnh vì mất kết nối. Mở người đó, xem thẻ máy: nếu ghi **Đang chờ gửi**, kiểm tra điện và dây mạng của máy rồi bấm **Thử lại ngay**. Nếu thẻ nhắc chạy So khớp, làm theo [So khớp 2 máy](../may-cham-cong/so-khop-2-may.md).

</details>

<details>

<summary>Khoá xong nhưng người đó vẫn còn trên máy</summary>

Đây là cách làm an toàn khi phần mềm chưa có đủ ảnh mặt hoặc vân tay như trên máy: người đó chỉ bị tắt hiệu lực, không ra vào được. Nếu muốn gỡ hẳn khỏi máy, dùng [Xoá hẳn một người](xoa-han.md).

</details>

<details>

<summary>Sau khi Mở lại, máy báo Có lỗi</summary>

Bấm **Chi tiết** để xem lý do rồi **Thử lại ngay**. Xem [Xử lý sự cố](../tham-khao/xu-ly-su-co.md).

</details>
