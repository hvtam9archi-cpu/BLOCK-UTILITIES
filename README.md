# TPL Block Utilities

**TPL Block Utilities** là một Plugin dành cho AutoCAD (hỗ trợ các phiên bản từ 2021 đến 2024+), được xây dựng trên nền tảng .NET 4.8. Plugin cung cấp một bộ công cụ mạnh mẽ và tối ưu để quản lý, hiệu chỉnh và tự động hóa các thao tác với Block trong bản vẽ AutoCAD.

Dự án đã được tái cấu trúc hoàn toàn theo quy trình phát triển hiện đại, sử dụng **WPF** cho giao diện đồ họa (thay thế cho WinForms cũ) và kiến trúc **SDK-style** cho việc quản lý mã nguồn.

---

## 🏗 Kiến Trúc Dự Án

Dự án tuân thủ chặt chẽ các nguyên tắc phát triển phần mềm và tối ưu hóa AutoCAD API:

1. **SDK-Style Project (`BLOCK UTILITIES.csproj`)**: Quản lý các tham chiếu NuGet gọn nhẹ, hỗ trợ TargetFramework `net48`.
2. **Giao diện WPF Hiện Đại**: Toàn bộ UI được nâng cấp sang nền tảng WPF với Dark Theme chuẩn hóa (màu sắc, viền bo tròn, các Controls tuỳ chỉnh) được quản lý qua `ThemeDictionary.xaml`.
3. **Decoupled Architecture**: 
   - `Commands.cs`: Đóng vai trò là Entry Point, chỉ tiếp nhận lệnh từ môi trường AutoCAD.
   - `BlockLogic.cs`: Xử lý toàn bộ "Core Logic", đảm bảo an toàn bộ nhớ (Transaction/DocumentLock) và thao tác với Database.
4. **Auto Deployment (CI/CD Local)**: Khi nhấn Build (F5), project tự động đóng gói cấu trúc thư mục Bundle và copy vào `%AppData%\Autodesk\ApplicationPlugins\BlockUtilities.bundle`, cho phép AutoCAD tự động nạp Plugin thông qua `PackageContents.xml` mà không cần gọi lệnh `NETLOAD` thủ công.
5. **Ribbon Integration**: Tự động chèn Menu Tab "TPL Block Utilities" vào giao diện Ribbon của AutoCAD sau khi tải xong.

---

## 🚀 Danh Sách Lệnh (Commands)

### Nhóm 1: Biến đổi hình học (Transformation)
* **`RSET`** - Mở bảng cài đặt toàn cục (Global Settings) cho khoảng giá trị Tỷ lệ (Scale) và Góc xoay (Rotation).
* **`RSC`** - Scale ngẫu nhiên các Block được chọn theo giới hạn trong `RSET`.
* **`RRT`** - Xoay ngẫu nhiên các Block được chọn theo giới hạn trong `RSET`.
* **`RAL`** - Scale và Xoay ngẫu nhiên (Align) đồng thời các Block.
* **`RR`** - Đưa (Reset) toàn bộ Block được chọn về trạng thái gốc (Góc xoay = 0, Tỷ lệ = 1).

### Nhóm 2: Quản lý & Thay thế Block
* **`DELB`** - Xóa nhanh các Block (xóa hiển thị và Purge định nghĩa) bằng cách chọn trực tiếp hoặc nhập tên.
* **`DLB`** - Quét và chuyển toàn bộ các thành phần con bên trong Block Definition (kể cả Dynamic Block) về **Layer 0** và Color **ByLayer**.
* **`UDLB`** - Hoàn tác (Undo) lại tác vụ `DLB` gần nhất.
* **`MU`** (Make Unique) - Tách riêng (Clone) các Block được chọn thành một loại Block hoàn toàn mới độc lập với bản gốc.

### Nhóm 3: Hiệu chỉnh Base Point (Điểm chèn)
* **`CB`** - Tự động dời Base Point của Block về chính tâm hình học (Center) và bù trừ vị trí để không bị dịch chuyển khi nhìn trên bản vẽ.
* **`CBP`** - Cho phép người dùng click chọn Base Point mới trên màn hình (thay vì tâm).
* **`CBPR`** - Đổi Base Point bằng click chuột, đồng thời giữ nguyên vị trí trực quan của Block.
* **`AB`** - (Auto Block Center) - Gom các đối tượng rời rạc đang được chọn thành một Block ngẫu nhiên có Base Point nằm tại trung tâm.
* **`JBP`** (Justify Base Point) - Mở giao diện lưới 3x3 để dời Base Point về 9 vị trí góc/cạnh tiêu chuẩn (Top-Left, Middle-Center, Bottom-Right...). Hỗ trợ lựa chọn giữa việc giữ nguyên điểm chèn hoặc giữ nguyên vị trí trực quan.

### Nhóm 4: Tiện ích khác
* **`RB`** (Rename Block) - Đổi tên Block đang chọn. Có hỗ trợ tự động tạo tên ngẫu nhiên chống trùng lặp.

---

## 🛠 Hướng Dẫn Cài Đặt & Phát Triển

### Yêu cầu hệ thống:
- AutoCAD 2021 trở lên (Khuyến nghị 2024).
- Visual Studio 2022.
- .NET Framework 4.8.

### Các bước Build:
1. Mở file Solution `BLOCK.slnx` bằng Visual Studio.
2. Chọn cấu hình **Debug** hoặc **Release**, nền tảng **x64**.
3. Nhấn **Build Solution (Ctrl + Shift + B)**.
4. Dự án sẽ tự động sao chép các tệp cần thiết (bao gồm `.dll` và `PackageContents.xml`) vào thư mục `ApplicationPlugins` của người dùng hiện tại.
5. Khởi động lại hoặc mở AutoCAD. Plugin sẽ tự động được tải (TPL Block Utilities Tab sẽ xuất hiện trên thanh Ribbon).

---

## 🎨 Hướng Dẫn Tùy Chỉnh Giao Diện
Tất cả các thành phần màu sắc được quản lý tại:
`BLOCK\Themes\ThemeDictionary.xaml`

Các thành phần cốt lõi:
- Nền chính: `#181A1F`
- Nền phụ (Panel): `#2B2D32`
- Accent (Xanh dương): `#2563EB`
- Typography: Roboto / Segoe UI, văn bản sáng `#E8EAED`.

*Nếu cần thay đổi UI, hãy mở các file XML `.xaml` nằm trong thư mục `UI/` để tinh chỉnh trực quan qua trình thiết kế của Visual Studio.*
