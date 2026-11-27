# BTL.MMT · ET4230 Đề 14 Chia sẻ tệp P2P bằng C#

> Repo chính là **kimsakota/BTL.MMT**. Lịch sử chuyển tài khoản nằm ở [ghi chú GitHub](docs/GitHub-tam-thoi.md).


Dự án của **Linh, Minh, Cường, Quân, Tú**, chuẩn bị ngày 23/10/2026. Mục tiêu là tự xây hệ thống truyền tệp TCP nhiều nguồn, chia sẻ lại phân mảnh đã xác minh và phục hồi khi peer rời mạng; áp dụng kiến thức buffer, I/O và ownership từ VstHelper.

CLI có bốn lệnh. `tracker` giới thiệu nguồn theo fileId. `seed` phục vụ tệp đầy đủ. `download` tải từ một nguồn để làm đối chứng client–server. `peer` tải từ nhiều nguồn và chia sẻ lại piece đã xác minh trong lúc tải. Cả hai chế độ chỉ công bố tệp sau khi SHA-256 toàn tệp khớp. Trạng thái và phạm vi kiểm chứng nằm ở [bộ nhớ dự án](.github/agent/CURRENT_STATE.md).

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

## Chạy P2P nhiều nguồn và thu số liệu

Từ thư mục gốc, build Release rồi chạy các lệnh sau với Python 3.11 trở lên:

```powershell
dotnet build ET4230.P2P.sln -c Release
python tools/run_experiments.py --prove-sharing
python tools/run_experiments.py --smoke
```

Lệnh đầu của runner chạy ba peer trên TCP loopback. B mở upload khi đã xác minh piece đầu tiên, C bắt đầu sau khi B đăng ký tracker. Kết quả đạt khi C xác minh dữ liệu từ A lẫn B, log của B cho thấy đã cấp piece cho C trong lúc B còn đang tải, và SHA-256 của cả hai tệp đầu ra khớp. Lệnh `--smoke` thử cả hai chế độ với 3 và 4 peer qua ba kịch bản ổn định, upload không đều và peer rời.

Chạy `python tools/run_experiments.py` để dùng ma trận trong [cấu hình thí nghiệm](configs/experiments.json). Mỗi lượt ghi config, stdout/stderr, kết quả SHA-256 và số byte payload từng nguồn trong `results/raw/`. `receivers.csv` giữ trạng thái và thời gian mỗi receiver. `summary.csv` có số ca hoàn tất/rời/lỗi/timeout, trung bình và độ lệch chuẩn của các receiver hoàn tất. `run.json` ghi thời gian theo đồng hồ của runner và lượng upload theo tiến trình. Runner trên một máy đo TCP loopback. Số đo LAN cần chạy trên hai máy và ghi topology tương ứng.

### Chạy ba peer trên hai máy LAN

1. Dùng JSON mẫu ở trên trên hai máy. Giả sử máy 1 có IP `192.168.1.10`, máy 2 có IP `192.168.1.11`. Dùng IP thật thay các giá trị mẫu. `tracker.host` của mọi peer trỏ về máy 1. `tracker.bindHost` trên máy 1 dùng IP của máy 1. Chạy tracker và seed A trên máy 1. Seed dùng `peer.listenHost` và `peer.advertiseHost` bằng IP máy 1, cổng 42301.
2. Đợi `SEED_READY` rồi chép **manifest** sinh ra sang thư mục config của B và C. Tạo config riêng B trên máy 2, dùng `peer.listenHost` và `peer.advertiseHost` bằng IP máy 2, cổng 42302, và `file.outputPath` là `received-b.bin`. Config C có thể đặt trên máy 1, dùng IP máy 1, cổng 42303 và `received-c.bin`. Dùng các đường dẫn đích mới cho mỗi lượt.
3. Đặt `strategy.applicationUploadLimitMbps` khoảng 8 cho seed và chọn tệp nhiều piece để B còn tải khi C bắt đầu. Biến này giới hạn payload do tiến trình phục vụ, đơn vị Mbit/s. Đặt `transfer.shareAfterCompletionSeconds` trong config B nếu muốn B tiếp tục phục vụ thêm sau khi công bố tệp. Lệnh cho B và C khác nhau ở file config.

```powershell
dotnet run -c Release --no-build --project src/ET4230.Cli -- peer --config demo/data/peer-b.json
dotnet run -c Release --no-build --project src/ET4230.Cli -- peer --config demo/data/peer-c.json
```

Chạy lệnh B trước. Đợi `PEER_READY` của B rồi chạy C ở terminal của máy còn lại. Lưu log `SEED_READY`, `PEER_READY`, `PIECE_VERIFIED`, `UPLOAD_BLOCK` và `DOWNLOAD_OK`, đồng thời so lại hash bằng `Get-FileHash`. Nếu bắt gói, lọc theo cổng tracker và các cổng peer thực tế. Dùng `download` với chỉ một seed đăng ký để chạy mẫu đối chứng.

Lượt tải mỗi lần chỉ có một BLOCK đang chờ. `strategy.pieceSelection` chọn `sequential` mặc định hoặc `rarest-first` theo BITFIELD của các nguồn đã kết nối. Khi nguồn lỗi, chương trình thử nguồn khác và tải lại từ đầu piece đang dở. Số nguồn đang kết nối và số phiên upload được giới hạn qua `transfer.maxSources` và `peer.maxUploadSessions`. Giữ cùng tệp, kích thước piece/block, topology và giới hạn seed khi so hai chế độ.

## Sơ đồ tài liệu và source

| Vùng | Trách nhiệm |
|---|---|
| `src/ET4230.Core/` | Giao thức, transport, storage, tracker/discovery và receiver tuần tự |
| `src/ET4230.Cli/` | Điều phối tracker, seed, download và peer |
| `src/ET4230.VstReference/` | Build wrapper cho source VstHelper, chưa được CLI/Core gọi |
| `references/VstHelper/` | 20 file C# và csproj gốc, giữ nguyên byte, có SHA-256 |
| `configs/` | Tham số peer, giới hạn buffer, danh sách nhóm và thiết kế thí nghiệm |
| `tests/` | xUnit, test giao thức và các ca TCP loopback |
| `tools/` | Kiểm tra cấu trúc, lập chỉ mục slide, chụp snapshot tham chiếu |
| `docs/` | [Mục lục](docs/README.md): học tập, thiết kế, triển khai, kiểm chứng và tham khảo |
| `.github/agent/` | Bộ nhớ ngắn, nguồn chuẩn và điểm tiếp tục |
| `results/`, `plots/`, `demo/` | Quy ước lưu bằng chứng khi có chạy thật |

Workflow `.github/workflows/validate.yml` kiểm tra cấu trúc, config, hash snapshot, build, xUnit và hai luồng CLI trên TCP loopback khi push hoặc mở PR. Xem trạng thái tại [GitHub Actions](https://github.com/kimsakota/BTL.MMT/actions).

Hướng dẫn cộng tác và GitHub Desktop nằm ở [CONTRIBUTING](CONTRIBUTING.md). Tài liệu tập trung theo nội dung dự án P2P; phân công và tiến độ dùng chung một [Kế hoạch](docs/Ke-hoach.md).
