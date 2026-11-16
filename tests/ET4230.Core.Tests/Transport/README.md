# Hướng dẫn đọc `FrameTransportTests`

File test nằm tại `tests/ET4230.Core.Tests/Transport/FrameTransportTests.cs`. Nó kiểm tra việc đóng khung, đọc/ghi frame trên stream và một phiên seed tối giản qua TCP loopback.

## Các file liên quan

- `src/ET4230.Core/Transport/FrameTransport.cs`: định nghĩa `Frame`, đọc đủ một frame từ stream và ghi header cùng payload ra stream.
- `src/ET4230.Core/Transport/SeedSession.cs`: xử lý phía seed: nhận HELLO, phản hồi HELLO_OK/BITFIELD và phục vụ REQUEST_BLOCK.
- `src/ET4230.Core/Protocol/HeaderCodec.cs`: encode/decode phần header 16 byte mà transport dùng.
- `tests/ET4230.Core.Tests/Protocol/HeaderCodecTests.cs`: kiểm tra codec riêng; không thay thế test transport.

`FrameTransportTests` không dùng file dữ liệu mẫu bên ngoài. Các byte đầu vào và expected được ghi trực tiếp trong test. Test loopback dùng `TestSeedSource`, nguồn dữ liệu giả trả về chuỗi `ABCD`; nó không đọc file thật hay tính hash.

## Các test hiện có

Có 5 hàm test. `RejectsEofInsideAFrame` có hai `[InlineData]`, vì vậy xUnit chạy tổng cộng **6 test case**.

### 1. `ReadsTwoFramesFromShortReadsAndThenCleanEof`

Đưa vào một stream chứa liên tiếp hai frame mẫu HAVE và REQUEST_BLOCK. `ShortReadStream` giới hạn mỗi lần đọc tối đa 3 byte để mô phỏng việc stream trả dữ liệu từng mảnh.

Kết quả mong đợi:

- Đọc frame HAVE với header `(version 1, type 4, requestId 0, payloadLength 4)` và payload hex `00000002`.
- Đọc frame REQUEST_BLOCK với header `(version 1, type 5, requestId 7, payloadLength 12)` và payload hex `000000020000000000004000`.
- Lần đọc tiếp theo gặp EOF sạch và trả `null`.

### 2. `RejectsEofInsideAFrame` — 2 ca

Hai chuỗi hex đầu vào cố ý kết thúc khi frame chưa đầy:

- `4534323301`: EOF giữa header.
- `453432330104000000000000000000040000`: header khai báo payload 4 byte nhưng stream chỉ có một phần payload.

Kết quả mong đợi ở cả hai ca là `EndOfStreamException`.

### 3. `RejectsInvalidHeaderBeforeReadingPayload`

Đưa header có payload length không hợp lệ vào stream. Kết quả mong đợi là `InvalidDataException`. Test còn kiểm tra `stream.Position == 16`, chứng minh transport đã đọc header rồi từ chối trước khi đọc phần tiếp theo.

### 4. `SendsAnEmptyCancelPayloadWithTheExpectedWireBytes`

Gọi `WriteAsync` để gửi frame CANCEL (type 7), request ID 7, payload rỗng.

Kết quả mong đợi là đúng chuỗi byte wire:

```text
45343233010700000000000700000000
```

Test sau đó đọc lại stream và xác nhận payload rỗng, rồi nhận `null` ở lần đọc tiếp theo.

### 5. `SeedReturnsAvailabilityAndBlockOverTcp`

Mở TCP listener trên loopback, chạy `SeedSession` ở một đầu và client giả lập ở đầu kia. Client gửi HELLO cho file có file ID khớp với `TestSeedSource`, rồi yêu cầu block đầu tiên.

Kết quả mong đợi:

- Seed gửi HELLO_OK với request ID giữ nguyên là 1.
- Seed gửi BITFIELD một byte `80`, nghĩa là piece 0 đang verified.
- Client yêu cầu piece 0, offset 0, length 4; seed trả BLOCK với request ID 2 và payload hex `00000000000000000000000441424344` (metadata 12 byte theo sau là dữ liệu `ABCD`).

Đây là socket TCP loopback thật, nhưng nguồn block là `TestSeedSource` giả. Test chưa kiểm tra disk storage, tracker, download đầy đủ hoặc kiểm tra SHA-256 của tệp nhận.

## Cách chạy

Từ thư mục gốc repository, chỉ chạy test lớp transport này:

```powershell
dotnet test tests/ET4230.Core.Tests/ET4230.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~FrameTransportTests
```

Chạy toàn bộ test trong project chung:

```powershell
dotnet test tests/ET4230.Core.Tests/ET4230.Core.Tests.csproj --configuration Release
```

`Passed` với `failed: 0` nghĩa là các hành vi được liệt kê ở trên đạt expected. Nó chưa chứng minh toàn bộ ứng dụng P2P hoặc truyền tệp từ disk hoạt động đúng.
