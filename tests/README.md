# Chạy kiểm thử Core

Test project dùng **xUnit 2** làm framework và nằm ở `tests/ET4230.Core.Tests/`. Đây là lựa chọn khởi đầu để có test chạy được; Linh có thể review framework trước khi merge. Project tham chiếu `src/ET4230.Core/`, nên test gọi implementation thật.

## Chạy test

Từ thư mục gốc repository:

```powershell
dotnet test tests/ET4230.Core.Tests/ET4230.Core.Tests.csproj --configuration Release
```

Hoặc chạy toàn solution:

```powershell
dotnet test ET4230.P2P.sln --configuration Release
```

Lần đầu chạy cần khôi phục các package NuGet của test project.

## Test hiện có

`ET4230.Core.Tests/Protocol/HeaderCodecTests.cs` kiểm tra:

- Encode header phải khớp byte độc lập trong `protocol-vectors.json`.
- Decode vector phải đọc đúng version, type, flags, request ID và payload length.
- Từ chối kích thước header sai, magic/version/type/flags sai, request ID sai và payload không hợp lệ.
- Encode từ chối header không hợp lệ và destination có kích thước khác 16 byte.

Vector là dữ liệu thiết kế, không phải capture mạng. Các test này kiểm tra codec/header, không kiểm tra socket, tracker hay truyền tệp P2P. Khi có module khác, tác giả module bổ sung test vào cùng project.

`tools/check_project.py` vẫn là checker cấu trúc/config/provenance; nó không thay thế các unit test trên.
