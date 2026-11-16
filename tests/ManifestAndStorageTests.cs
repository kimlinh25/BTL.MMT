using System.Security.Cryptography;
using System.Text;
using ET4230.Core.Storage;
using Xunit;

namespace ET4230.Core.Tests.Storage;

public class ManifestAndStorageTests : IDisposable
{
    private readonly string _tempDir;

    public ManifestAndStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ET4230_W03_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task CreateManifest_And_OutOfOrderWrite_WithTailBlock_VerifiesSuccessfully()
    {
        // Tệp 19 byte, P = 8, B = 4 (3 piece: 8, 8, 3 byte; block cuối dài 3 byte)
        string seedPath = Path.Combine(_tempDir, "seed_19bytes.bin");
        string destPath = Path.Combine(_tempDir, "receiver_chosen_name.bin");
        byte[] originalData = Encoding.ASCII.GetBytes("0123456789ABCDEFGHI"); // 19 bytes
        await File.WriteAllBytesAsync(seedPath, originalData);

        var manifest = await FileManifest.CreateFromFileAsync(seedPath, pieceLength: 8, blockLength: 4);
        Assert.Equal(19, manifest.FileSize);
        Assert.Equal(3, manifest.PieceHashes.Count);

        using var seedStorage = new FileStorage(seedPath, manifest, isSeed: true);
        using var receiverStorage = new FileStorage(destPath, manifest, isSeed: false);

        // Ghi Piece 2 (tail piece: chỉ có 1 block 3 byte tại offset 16) trước
        var (readTailRes, tailData) = await seedStorage.ReadBlockAsync(2, 0);
        Assert.Equal(StorageStatus.Ok, readTailRes.Status);
        Assert.Equal(3, tailData.Length);

        var writeTailRes = await receiverStorage.WriteBlockAsync(2, 0, tailData);
        Assert.Equal(StorageStatus.Ok, writeTailRes.Status);
        Assert.Equal(StorageStatus.Verified, (await receiverStorage.VerifyPieceAsync(2)).Status);

        // Ghi Piece 0 và Piece 1 ngược thứ tự block (block 1 trước, block 0 sau)
        for (int p = 0; p <= 1; p++)
        {
            var (_, b1) = await seedStorage.ReadBlockAsync(p, 1);
            var (_, b0) = await seedStorage.ReadBlockAsync(p, 0);

            await receiverStorage.WriteBlockAsync(p, 1, b1);
            // Mới ghi 1 block thì VerifyPiece phải báo IncompletePiece, không được báo Verified
            Assert.Equal(StorageStatus.IncompletePiece, (await receiverStorage.VerifyPieceAsync(p)).Status);

            await receiverStorage.WriteBlockAsync(p, 0, b0);
            Assert.Equal(StorageStatus.Verified, (await receiverStorage.VerifyPieceAsync(p)).Status);
        }

        // Kiểm tra toàn tệp sau khi đủ cả 3 piece
        var fullVerify = await receiverStorage.VerifyFullFileAsync();
        Assert.Equal(StorageStatus.Verified, fullVerify.Status);
        Assert.Equal(originalData, await File.ReadAllBytesAsync(destPath));
    }

    [Fact]
    public async Task CorruptedBlock_ReturnsHashMismatch_evenWhenByteCountIsComplete()
    {
        string seedPath = Path.Combine(_tempDir, "seed_corrupt.bin");
        string destPath = Path.Combine(_tempDir, "dest_corrupt.bin");
        byte[] data = Encoding.ASCII.GetBytes("ABCDEFGH"); // 8 bytes, P = 8, B = 4
        await File.WriteAllBytesAsync(seedPath, data);

        var manifest = await FileManifest.CreateFromFileAsync(seedPath, 8, 4);
        using var receiverStorage = new FileStorage(destPath, manifest);

        // Ghi đủ 8 byte nhưng làm sai lệch 1 byte ở block 1
        await receiverStorage.WriteBlockAsync(0, 0, Encoding.ASCII.GetBytes("ABCD"));
        await receiverStorage.WriteBlockAsync(0, 1, Encoding.ASCII.GetBytes("EFGZ")); // 'Z' thay vì 'H'

        var verifyRes = await receiverStorage.VerifyPieceAsync(0);
        Assert.Equal(StorageStatus.HashMismatch, verifyRes.Status);
        Assert.False(receiverStorage.IsPieceVerified(0));
        // Sau khi HashMismatch, trạng thái written của piece phải bị reset để tải lại
        Assert.False(receiverStorage.IsPieceFullyWritten(0));
    }

    [Fact]
    public async Task EmptyFile_ZeroBytes_VerifiesCorrectly()
    {
        string seedPath = Path.Combine(_tempDir, "empty_seed.bin");
        string destPath = Path.Combine(_tempDir, "empty_dest.bin");
        await File.WriteAllBytesAsync(seedPath, Array.Empty<byte>());

        var manifest = await FileManifest.CreateFromFileAsync(seedPath, 8, 4);
        Assert.Equal(0, manifest.FileSize);
        Assert.Empty(manifest.PieceHashes);

        using var receiverStorage = new FileStorage(destPath, manifest);
        var fullVerify = await receiverStorage.VerifyFullFileAsync();
        Assert.Equal(StorageStatus.Verified, fullVerify.Status);
        Assert.Equal(0, new FileInfo(destPath).Length);
    }

    [Fact]
    public async Task InvalidRange_Or_WrongBlockLength_ReturnsInvalidRange()
    {
        string seedPath = Path.Combine(_tempDir, "range_seed.bin");
        string destPath = Path.Combine(_tempDir, "range_dest.bin");
        await File.WriteAllBytesAsync(seedPath, new byte[19]);

        var manifest = await FileManifest.CreateFromFileAsync(seedPath, 8, 4);
        using var storage = new FileStorage(destPath, manifest);

        // Tail block (piece 2, block 0) dài 3 byte, cố tình truyền 4 byte -> báo lỗi InvalidRange
        var badLenRes = await storage.WriteBlockAsync(2, 0, new byte[4]);
        Assert.Equal(StorageStatus.InvalidRange, badLenRes.Status);

        // Piece 3 không tồn tại
        var outOfBoundsRes = await storage.WriteBlockAsync(3, 0, new byte[4]);
        Assert.Equal(StorageStatus.InvalidRange, outOfBoundsRes.Status);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* Bỏ qua lỗi dọn thư mục tạm */ }
    }
}
