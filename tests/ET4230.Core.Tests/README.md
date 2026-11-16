# ET4230.Core.Tests

Project xUnit kiểm tra các API trong `src/ET4230.Core/`. Mỗi module có file test riêng; `dotnet test` trong project này chạy tất cả.

- `Protocol/HeaderCodecTests.cs` — encode/decode header; xem [hướng dẫn](Protocol/README.md).
- `Storage/PieceLayoutTests.cs` — tính layout piece/block; xem [hướng dẫn](Storage/README.md).
- `Transport/FrameTransportTests.cs` — đọc/ghi frame và seed loopback; xem [hướng dẫn](Transport/README.md).

Chạy từ thư mục gốc repository:

```powershell
dotnet test tests/ET4230.Core.Tests/ET4230.Core.Tests.csproj --configuration Release
```
