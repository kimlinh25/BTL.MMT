# Hướng dẫn đọc `HeaderCodecTests`

File test nằm tại `tests/ET4230.Core.Tests/Protocol/HeaderCodecTests.cs`. Nó kiểm tra các phép chuyển đổi header 16 byte của giao thức v1 bằng cách gọi trực tiếp `HeaderCodec`; test không mở socket và không gửi dữ liệu qua mạng.

## Các file liên quan

- `src/ET4230.Core/Protocol/FrameHeader.cs`: kiểu dữ liệu chứa version, type, flags, request ID và payload length.
- `src/ET4230.Core/Protocol/HeaderCodec.cs`: mã được kiểm tra; encode `FrameHeader` thành 16 byte và decode 16 byte thành header.
- `src/ET4230.Core/Protocol/HeaderDecodeError.cs`: các mã lỗi mà decode có thể trả về.
- `tests/protocol-vectors.json`: hai vector header mẫu cố định.
- `tests/ET4230.Core.Tests/ET4230.Core.Tests.csproj`: tham chiếu Core và chép JSON vào thư mục output để test đọc được.

## Ý tưởng kiểm thử

Mỗi ca có đầu vào, gọi hàm cần kiểm tra, rồi so kết quả thực tế với kết quả mong đợi bằng các assertion của xUnit. Dữ liệu mong đợi của encode là chuỗi hex cố định trong JSON. Các trường hợp sai được tạo trong `HeaderCodecTests.cs`.

### Vector trong `protocol-vectors.json`

JSON ghi mục đích là vector thiết kế, không phải traffic capture hay kết quả test. Hiện có:

| Tên | Type | Request ID | Payload length | Header hex mong đợi |
|---|---:|---:|---:|---|
| `HAVE-piece-2` | 4 | 0 | 4 | `45343233010400000000000000000004` |
| `REQUEST_BLOCK-piece-2` | 5 | 7 | 12 | `4534323301050000000000070000000c` |

Header gồm 16 byte: magic `E423`, version, type, flags, request ID và payload length. `payloadHex` trong JSON mô tả payload ví dụ; `HeaderCodecTests` hiện chỉ dùng `headerHex`, không kiểm tra payload.

`LoadProtocolVectors()` đọc `protocol-vectors.json` bằng đường dẫn `AppContext.BaseDirectory`. Cấu hình `<Content ... CopyToOutputDirectory="PreserveNewest" />` trong csproj chép file JSON cạnh assembly test khi build. Hàm lấy tên và `headerHex`, sau đó đọc type, request ID và payload length trực tiếp từ các byte header bằng `BinaryPrimitives`. Vì vậy expected byte cho encode là vector cố định; expected field cho decode được rút ra từ chính header vector.

## Toàn bộ 29 test case

Các hàm có `[Theory]` được xUnit chạy một lần cho mỗi bộ dữ liệu. Tổng số hiện tại là 29:

### 1. `EncodeHeader_matches_independent_protocol_vector` — 2 ca

Với mỗi vector, test tạo `FrameHeader`, gọi `EncodeHeader`, đổi 16 byte kết quả thành hex chữ thường, rồi so với `headerHex` cố định trong JSON. Kết quả mong đợi là hai chuỗi byte khớp chính xác.

### 2. `TryDecodeHeader_reads_protocol_vector_fields` — 2 ca

Với mỗi vector, test đổi `headerHex` thành byte rồi gọi `TryDecodeHeader`. Kết quả mong đợi: trả `true`, lỗi là `HeaderDecodeError.None`, và header trả về khớp version 1, type, request ID, flags 0 và payload length của vector.

### 3. `TryDecodeHeader_rejects_malformed_headers` — 15 ca

Mỗi ca phải trả `false`, trả đúng mã lỗi dự kiến và đặt header đầu ra về `default`.

| Đầu vào lỗi | Mã lỗi mong đợi |
|---|---|
| Header ngắn 15 byte | `InvalidHeaderSize` |
| Header dài 17 byte | `InvalidHeaderSize` |
| Magic sai | `InvalidMagic` |
| Version 2 không hỗ trợ | `UnsupportedVersion` |
| Type 9 không biết | `UnknownType` |
| Flags khác 0 | `InvalidFlags` |
| Request type có request ID bằng 0 | `InvalidRequestId` |
| Notification HAVE có request ID khác 0 | `InvalidRequestId` |
| Payload HAVE ngắn hơn 4 byte | `InvalidPayloadLength` |
| Payload REQUEST_BLOCK không đúng 12 byte | `InvalidPayloadLength` |
| BLOCK chỉ có metadata, không có data | `InvalidPayloadLength` |
| CANCEL có payload | `InvalidPayloadLength` |
| Payload control rỗng | `InvalidPayloadLength` |
| Payload control vượt 64 KiB (65,537 byte) | `InvalidPayloadLength` |
| Payload vượt 1 MiB (1,048,577 byte) | `InvalidPayloadLength` |

### 4. `EncodeHeader_rejects_invalid_header_fields` — 7 ca

Mỗi ca đưa một `FrameHeader` không hợp lệ vào `EncodeHeader`. Kết quả mong đợi là ném `ArgumentException`:

- Version không hỗ trợ.
- Type không biết.
- Flags khác 0.
- Notification có request ID khác 0.
- Request không có request ID.
- Payload vượt 1 MiB.
- Độ dài payload không hợp lệ với type HAVE.

### 5. `EncodeHeader_requires_exactly_sixteen_destination_bytes` — 3 ca

Test đưa destination có độ dài 0, 15 và 17 byte vào `EncodeHeader`. Mỗi trường hợp phải ném `ArgumentException`, vì header phải có đúng 16 byte.

Tổng: `2 + 2 + 15 + 7 + 3 = 29` test case. Đây là số lần xUnit chạy các bộ dữ liệu, không phải số hàm test.

## Cách chạy

Từ thư mục gốc repository, chỉ chạy các test trong lớp này:

```powershell
dotnet test tests/ET4230.Core.Tests/ET4230.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~HeaderCodecTests
```

Chạy toàn bộ test trong project chung:

```powershell
dotnet test tests/ET4230.Core.Tests/ET4230.Core.Tests.csproj --configuration Release
```

`Passed` với `failed: 0` nghĩa là các ca đã chạy không phát hiện sai lệch trong phạm vi trên. Nó không chứng minh socket, transport, tracker hay truyền tệp hoạt động đúng.
