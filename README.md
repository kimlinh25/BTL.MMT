# BTL.MMT · ET4230 Đề 14 Chia sẻ tệp P2P bằng C#

> Nhóm làm tiếp trên **kimlinh25/BTL.MMT** từ 07/11/2026. Xem [hướng dẫn repo tạm và quay về tài khoản cũ](docs/GitHub-tam-thoi.md).


Dự án của **Linh, Minh, Cường, Quân, Tú**, chuẩn bị ngày 23/10/2026. Mục tiêu là tự xây hệ thống truyền tệp TCP nhiều nguồn, chia sẻ lại phân mảnh đã xác minh và phục hồi khi peer rời mạng; áp dụng kiến thức buffer, I/O và ownership từ VstHelper.

**W03 có đường tải tệp TCP từ một nguồn.** CLI chạy tracker, seed và download. Receiver lấy địa chỉ seed từ tracker, nhận từng block, xác minh SHA-256 và công bố tệp hoàn chỉnh. Trạng thái và phạm vi kiểm chứng nằm ở [bộ nhớ dự án](.github/agent/CURRENT_STATE.md).

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

`global.json` chọn SDK .NET 10 stable, cho phép bản feature mới hơn cùng major. Máy Linh được kiểm tra có SDK 10.0.301. Hướng dẫn ở [thiết lập môi trường](docs/trien-khai/Thiet-lap-moi-truong.md).

## Chạy lại W03 trên một máy

Chạy build Release từ thư mục gốc. Để tự kiểm tra ba tiến trình CLI qua TCP loopback trên Windows, chạy:

```powershell
dotnet build ET4230.P2P.sln -c Release
dotnet test tests/ET4230.Core.Tests/ET4230.Core.Tests.csproj -c Release --no-build
pwsh -File tools/test_w03_cli.ps1
```

Script kiểm tra tệp 196.611 byte có block cuối lẻ, tệp rỗng, seed còn được tìm thấy sau một khoảng TTL nhờ heartbeat, và việc giữ nguyên tệp đích khi chạy lại. Script in SHA-256 tính bằng `Get-FileHash` và đường dẫn log/cấu hình trong `tmp/`.

Để chạy thủ công, tạo `demo/data/w03.json` và đặt một tệp bất kỳ tên `source.bin` trong cùng thư mục:

```json
{
  "schemaVersion": 1,
  "tracker": {
    "bindHost": "127.0.0.1", "host": "127.0.0.1", "port": 42300,
    "heartbeatSeconds": 5, "ttlSeconds": 20
  },
  "peer": { "listenHost": "127.0.0.1", "advertiseHost": "127.0.0.1", "port": 42301 },
  "transfer": { "pieceSizeBytes": 65536, "blockSizeBytes": 4096, "requestTimeoutSeconds": 15 },
  "file": { "sourcePath": "source.bin", "manifestPath": "manifest.json", "outputPath": "received.bin" }
}
```

Mở ba terminal tại thư mục gốc. Đợi `TRACKER_READY` trước khi chạy seed, và `SEED_READY` trước khi chạy download.

```powershell
# Terminal 1
dotnet run -c Release --no-build --project src/ET4230.Cli -- tracker --config demo/data/w03.json
# Terminal 2
dotnet run -c Release --no-build --project src/ET4230.Cli -- seed --config demo/data/w03.json
# Terminal 3
dotnet run -c Release --no-build --project src/ET4230.Cli -- download --config demo/data/w03.json
Get-FileHash demo/data/source.bin, demo/data/received.bin -Algorithm SHA256
```

Đường dẫn tệp được giải tương đối theo thư mục chứa JSON. Seed tạo manifest v1 nếu chưa có hoặc kiểm tệp nguồn theo manifest có sẵn. Khi chạy khác máy, chuyển manifest tin cậy sang bên tải và cấu hình địa chỉ kết nối tương ứng. Lần tải thành công in `DOWNLOAD_OK`, trả mã 0 và công bố tệp đích mới sau khi hash khớp. Lỗi chạy trả mã 1, sai cú pháp hoặc cấu hình trả mã 2. Dùng Ctrl+C để dừng tracker và seed. CI xác nhận TCP loopback trên một máy Windows.

## Sơ đồ tài liệu và source

| Vùng | Trách nhiệm |
|---|---|
| `src/ET4230.Core/` | Giao thức, transport, storage, tracker/discovery và receiver tuần tự |
| `src/ET4230.Cli/` | Điều phối ba lệnh tracker, seed và download |
| `src/ET4230.VstReference/` | Build wrapper cho source VstHelper, chưa được CLI/Core gọi |
| `references/VstHelper/` | 20 file C# và csproj gốc, giữ nguyên byte, có SHA-256 |
| `configs/` | Tham số peer, giới hạn buffer, danh sách nhóm và thiết kế thí nghiệm |
| `tests/` | xUnit, test giao thức và các ca TCP loopback |
| `tools/` | Kiểm tra cấu trúc, lập chỉ mục slide, chụp snapshot tham chiếu |
| `docs/` | [Mục lục](docs/README.md): học tập, thiết kế, triển khai, kiểm chứng và tham khảo |
| `.github/agent/` | Bộ nhớ ngắn, nguồn chuẩn và điểm tiếp tục |
| `results/`, `plots/`, `demo/` | Quy ước lưu bằng chứng khi có chạy thật |

Workflow `.github/workflows/validate.yml` kiểm tra cấu trúc, config, hash snapshot, build, xUnit và luồng CLI W03 trên Windows khi push hoặc mở PR. Xem trạng thái tại [GitHub Actions](https://github.com/kimlinh25/BTL.MMT/actions).

Hướng dẫn cộng tác và GitHub Desktop nằm ở [CONTRIBUTING](CONTRIBUTING.md). Tài liệu tập trung theo nội dung dự án P2P; phân công và tiến độ dùng chung một [Kế hoạch](docs/Ke-hoach.md).
