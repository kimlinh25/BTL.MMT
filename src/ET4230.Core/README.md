# ET4230.Core

Core chứa logic và API của ứng dụng P2P; CLI nhận lệnh người dùng rồi gọi các API này. Core không chứa giao diện dòng lệnh.

## Module hiện có

- `Protocol/`: mô tả header 16 byte và encode/decode header v1.
- `Storage/PieceLayout.cs`: tính số lượng, vị trí và kích thước piece/block; chưa đọc hoặc ghi tệp.
- `Transport/FrameTransport.cs`: đọc/ghi frame trên một `Stream`.
- `Transport/SeedSession.cs`: xử lý HELLO, BITFIELD và yêu cầu block ở phía seed qua stream, nhận nguồn dữ liệu qua `ISeedSource`.

Đây là các module nền; chúng chưa tạo thành luồng tải tệp hoàn chỉnh. Tracker, downloader và kết nối storage thật chưa được nối trong Core hiện tại.

Test dùng chung nằm trong `tests/ET4230.Core.Tests/`. Hướng dẫn chi tiết theo module ở `tests/ET4230.Core.Tests/{Protocol,Storage,Transport}/README.md`.
