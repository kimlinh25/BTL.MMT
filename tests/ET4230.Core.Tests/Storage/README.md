# Hướng dẫn đọc `PieceLayoutTests`

File test nằm tại `tests/ET4230.Core.Tests/Storage/PieceLayoutTests.cs`. Nó kiểm tra phép tính chia tệp thành piece và block của `src/ET4230.Core/Storage/PieceLayout.cs`. `PieceLayout` chỉ tính số lượng, vị trí và độ dài; nó không đọc/ghi tệp, không truyền TCP và không tính SHA-256.

## Các khái niệm

- **Tệp**: dãy byte cần chia.
- **Piece**: phần lớn của tệp, tối đa `PieceLength` byte.
- **Block**: phần nhỏ bên trong piece, tối đa `BlockLength` byte.
- **FileOffset**: vị trí block tính từ đầu tệp.
- **OffsetInPiece**: vị trí block tính từ đầu piece.

Tệp cuối có thể không vừa trọn piece hoặc block; phần cuối ngắn hơn được giữ nguyên, không tự thêm byte.

## Các test hiện có

File có 6 hàm `[Fact]`, nên có 6 test case. Mỗi test tạo `PieceLayout`, gọi thuộc tính/hàm cần kiểm tra, rồi dùng `Assert` so kết quả với số mong đợi.

### 1. `Test_W01_Case_21_8_4`

**Đầu vào:** file 21 byte, piece 8 byte, block 4 byte.

**Kết quả mong đợi:** 3 piece; piece cuối (index 2) dài 5 byte. Block index 1 của piece cuối bắt đầu tại `FileOffset = 20`, có `OffsetInPiece = 4` và `Length = 1`.

```text
Piece 0: file byte  0–7  (block 0: 0–3, block 1: 4–7)
Piece 1: file byte  8–15 (block 0: 8–11, block 1: 12–15)
Piece 2: file byte 16–20 (block 0: 16–19, block 1: 20)
```

### 2. `Test_New_Input_Case_19_8_4`

**Đầu vào:** file 19 byte, piece 8 byte, block 4 byte.

**Kết quả mong đợi:** 3 piece; piece cuối dài 3 byte và có 1 block. Block đó bắt đầu tại `FileOffset = 16`, dài 3 byte. Test không kiểm tra `OffsetInPiece` ở ca này.

### 3. `Test_EmptyFile_0_Bytes_ShouldHaveZeroPieces`

**Đầu vào:** file rỗng, piece 8 byte, block 4 byte.

**Kết quả mong đợi:** `PieceCount` bằng 0. Vì không tồn tại piece 0, gọi `GetPieceLength(0)` hoặc `GetBlockRange(0, 0)` phải ném `ArgumentOutOfRangeException`.

### 4. `Test_Protocol_V1_Constraints_ThrowsArgumentException`

Kiểm tra constructor từ chối hai cấu hình không hợp lệ theo ràng buộc v1:

- Piece 4 byte nhỏ hơn block 8 byte.
- Piece 10 byte không chia hết cho block 4 byte.

Mỗi cấu hình phải ném `ArgumentException`.

### 5. `Test_PieceCount_Overflow_ThrowsOverflowException`

**Đầu vào:** file lớn hơn `int.MaxValue` byte, piece 1 byte và block 1 byte.

Số piece cần thiết vượt giới hạn `int` mà thuộc tính `PieceCount` có thể chứa, nên constructor phải ném `OverflowException` thay vì trả về số bị tràn.

### 6. `Test_Divisible_Case_16_8_4`

**Đầu vào:** file 16 byte, piece 8 byte, block 4 byte.

**Kết quả mong đợi:** file chia vừa đủ thành 2 piece, mỗi piece dài 8 byte. Ca này kiểm tra trường hợp không có piece cuối ngắn.

## Cách chạy

Từ thư mục gốc repository, chỉ chạy test `PieceLayoutTests`:

```powershell
dotnet test tests/ET4230.Core.Tests/ET4230.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~PieceLayoutTests
```

Chạy toàn bộ test trong project chung:

```powershell
dotnet test tests/ET4230.Core.Tests/ET4230.Core.Tests.csproj --configuration Release
```

`Passed` với `failed: 0` nghĩa là các phép tính trong 6 tình huống này không sai lệch so với kết quả mong đợi. Nó chưa kiểm tra việc ghi tệp thật, truyền mạng, tính hash hay toàn bộ ứng dụng P2P.
