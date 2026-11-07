# BTL.MMT · ET4230 Đề 14 Chia sẻ tệp P2P bằng C#

> Nhóm làm tiếp trên **kimlinh25/BTL.MMT** từ 07/11/2026. Xem [hướng dẫn repo tạm và quay về tài khoản cũ](docs/GitHub-tam-thoi.md).


Dự án của **Linh, Minh, Cường, Quân, Tú**, chuẩn bị ngày 23/10/2026. Mục tiêu là tự xây hệ thống truyền tệp TCP nhiều nguồn, chia sẻ lại phân mảnh đã xác minh và phục hồi khi peer rời mạng; áp dụng kiến thức buffer, I/O và ownership từ VstHelper.

**Hiện tại là bộ khởi tạo để học và triển khai.** Solution .NET 10 có Core chưa có implementation, CLI chỉ hỗ trợ `--help`/`--version`, và wrapper biên dịch snapshot VstHelper. Chưa có tracker, peer, truyền tệp, kiểm thử mạng hoặc kết quả benchmark. Trạng thái kiểm chứng mới nhất nằm ở [bộ nhớ dự án](agent/CURRENT_STATE.md).

[Kiểm tra bộ khởi tạo](docs/trien-khai/Kiem-tra-bo-khoi-tao.md): build Debug/Release thành công, CLI trả exit code đúng; 10 warning CS8500 của Vst được giữ để audit. Repository: [kimsakota/BTL.MMT](https://github.com/kimsakota/BTL.MMT), nhánh chính `main`.

Linh xác nhận C# được phép dùng ngày 23/10/2026. Phân công và thiết kế v1 là đề xuất nội bộ cho nhóm xem xét; chưa tự ghi thành công việc đã nhận hoặc hoàn thành. Chưa có mã nhóm chính thức, đầy đủ họ tên/MSSV và ngày nộp.

## Bắt đầu

Clone bằng GitHub Desktop hoặc terminal:

```powershell
git clone https://github.com/kimsakota/BTL.MMT.git
cd BTL.MMT
```

1. Đọc [Tổng quan](docs/Tong-quan.md) để hiểu yêu cầu môn học và phạm vi đề xuất.
2. Mở [workspace](BTL.code-workspace) bằng VS Code hoặc `ET4230.P2P.sln` bằng Visual Studio; làm theo [thiết lập môi trường](docs/trien-khai/Thiet-lap-moi-truong.md) và chạy các lệnh dưới đây.
3. Mở [mục lục tài liệu](docs/README.md), chọn bài học hoặc hợp đồng liên quan đến task trong [Kế hoạch](docs/Ke-hoach.md).

Người mới bắt đầu từ [học liệu nền tảng](docs/hoc-tap/README.md), có giải thích, ví dụ và bài tập tự đối chiếu. [Lộ trình chuyên môn](docs/hoc-tap/Lo-trinh-theo-chuyen-mon.md) giúp chọn kiến thức riêng; Linh tự giao việc và bài đọc mỗi tuần theo nền của người nhận. Việc đọc bài, nhận task và hoàn thành implementation được ghi nhận riêng.

## Kiểm tra bộ khởi tạo

Chạy từ thư mục gốc repository, trong PowerShell hoặc terminal của IDE:

```powershell
dotnet --info
dotnet build ET4230.P2P.sln --configuration Debug
dotnet run --project src/ET4230.Cli -- --help
dotnet run --project src/ET4230.Cli -- --version
python tools/check_project.py
```

Lệnh Python cuối cần Python 3.10+ và chỉ dùng standard library; có thể dùng `py -3` nếu máy đăng ký Python Launcher. Việc trích PDF bằng `tools/extract_slides.py` là công việc nghiên cứu riêng, cần pypdf và Poppler; không phải dependency của ứng dụng hay bước bắt buộc để build.

`global.json` chọn SDK .NET 10 stable, cho phép bản feature mới hơn cùng major. Máy Linh được kiểm tra có SDK 10.0.301; máy khác cần tự kiểm tra. Hướng dẫn ở [thiết lập môi trường](docs/trien-khai/Thiet-lap-moi-truong.md).

## Sơ đồ tài liệu và source

| Vùng | Trách nhiệm |
|---|---|
| `src/ET4230.Core/` | Thư viện ứng dụng sẽ được nhóm tự viết theo module |
| `src/ET4230.Cli/` | Entry point console; hiện chỉ là bootstrap |
| `src/ET4230.VstReference/` | Build wrapper cho source VstHelper, chưa được CLI/Core gọi |
| `references/VstHelper/` | 20 file C# và csproj gốc, giữ nguyên byte, có SHA-256 |
| `configs/` | Tham số peer, giới hạn buffer, danh sách nhóm và thiết kế thí nghiệm |
| `tests/` | Kế hoạch test và vector giao thức; chưa có test ứng dụng |
| `tools/` | Kiểm tra cấu trúc, lập chỉ mục slide, chụp snapshot tham chiếu |
| `docs/` | [Mục lục](docs/README.md): học tập, thiết kế, triển khai, kiểm chứng và tham khảo |
| `agent/` | Bộ nhớ ngắn, nguồn chuẩn và điểm tiếp tục |
| `results/`, `plots/`, `demo/` | Quy ước lưu bằng chứng khi có chạy thật |

Workflow `.github/workflows/validate.yml` kiểm tra cấu trúc, config, hash snapshot và build/bootstrap trên Windows khi push hoặc mở PR. Xem trạng thái thực tế ở [GitHub Actions](https://github.com/kimsakota/BTL.MMT/actions); workflow này chưa thay kiểm thử ứng dụng P2P.

Hướng dẫn cộng tác và GitHub Desktop nằm ở [CONTRIBUTING](CONTRIBUTING.md). Tài liệu tập trung theo nội dung dự án P2P; phân công và tiến độ dùng chung một [Kế hoạch](docs/Ke-hoach.md).
