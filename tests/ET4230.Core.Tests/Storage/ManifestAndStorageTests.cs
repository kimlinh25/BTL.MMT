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
        string seedPath = Path.Combine(_tempDir, "seed_19bytes.bin");
        string destPath = Path.Combine(_tempDir, "receiver_chosen_name.bin");
        byte[] originalData = Encoding.ASCII.GetBytes("0123456789ABCDEFGHI"); // 19 bytes
        await File.WriteAllBytesAsync(seedPath, originalData);

        var manifest = await FileManifest.CreateFromFileAsync(seedPath, pieceLength: 8, blockLength: 4);
        Assert.Equal(19, manifest.FileSizeBytes);
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
            Assert.Equal(StorageStatus.IncompletePiece, (await receiverStorage.VerifyPieceAsync(p)).Status);

            await receiverStorage.WriteBlockAsync(p, 0, b0);
            Assert.Equal(StorageStatus.Verified, (await receiverStorage.VerifyPieceAsync(p)).Status);
        }

        var fullVerify = await receiverStorage.VerifyFullFileAsync();
        Assert.Equal(StorageStatus.Verified, fullVerify.Status);

        receiverStorage.Dispose();
        Assert.Equal(originalData, await File.ReadAllBytesAsync(destPath));
    }

    [Fact]
    public async Task CorruptedBlock_ReturnsHashMismatch_evenWhenByteCountIsComplete()
    {
        string seedPath = Path.Combine(_tempDir, "seed_corrupt.bin");
        string destPath = Path.Combine(_tempDir, "dest_corrupt.bin");
        byte[] data = Encoding.ASCII.GetBytes("ABCDEFGH");
        await File.WriteAllBytesAsync(seedPath, data);

        var manifest = await FileManifest.CreateFromFileAsync(seedPath, 8, 4);
        using var receiverStorage = new FileStorage(destPath, manifest);

        await receiverStorage.WriteBlockAsync(0, 0, Encoding.ASCII.GetBytes("ABCD"));
        await receiverStorage.WriteBlockAsync(0, 1, Encoding.ASCII.GetBytes("EFGZ")); // 'Z' thay vì 'H'

        var verifyRes = await receiverStorage.VerifyPieceAsync(0);
        Assert.Equal(StorageStatus.HashMismatch, verifyRes.Status);
        Assert.False(receiverStorage.IsPieceVerified(0));
        Assert.False(receiverStorage.IsPieceFullyWritten(0));
    }

    [Fact]
    public async Task EmptyFile_ZeroBytes_VerifiesCorrectly()
    {
        string seedPath = Path.Combine(_tempDir, "empty_seed.bin");
        string destPath = Path.Combine(_tempDir, "empty_dest.bin");
        await File.WriteAllBytesAsync(seedPath, Array.Empty<byte>());

        var manifest = await FileManifest.CreateFromFileAsync(seedPath, 8, 4);
        Assert.Equal(0, manifest.FileSizeBytes);
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

        var badLenRes = await storage.WriteBlockAsync(2, 0, new byte[4]);
        Assert.Equal(StorageStatus.InvalidRange, badLenRes.Status);

        var outOfBoundsRes = await storage.WriteBlockAsync(3, 0, new byte[4]);
        Assert.Equal(StorageStatus.InvalidRange, outOfBoundsRes.Status);
    }

    [Fact]
    public async Task ManifestV1_Deserializes_V1JsonFields_And_NormalizesUppercaseFileIdForSeed()
    {
        string seedPath = Path.Combine(_tempDir, "v1_seed.bin");
        byte[] content = Encoding.ASCII.GetBytes("ABCDEFGH"); // 8 bytes
        await File.WriteAllBytesAsync(seedPath, content);

        string shaUpper = Convert.ToHexString(SHA256.HashData(content)).ToUpperInvariant();
        string v1Json = $$"""
        {
          "version": 1,
          "fileId": "{{shaUpper}}",
          "displayName": "sample-v1.bin",
          "fileSizeBytes": 8,
          "pieceSizeBytes": 8,
          "blockSizeBytes": 4,
          "pieceHashes": [
            "{{shaUpper}}"
          ]
        }
        """;

        var manifest = FileManifest.FromJson(v1Json);
        Assert.Equal("sample-v1.bin", manifest.DisplayName);
        Assert.Equal(8, manifest.FileSizeBytes);
        Assert.Equal(8, manifest.PieceSizeBytes);
        Assert.Equal(4, manifest.BlockSizeBytes);

        // Kiểm tra fileId và pieceHashes viết hoa đã được chuẩn hóa về chữ thường
        Assert.Equal(shaUpper.ToLowerInvariant(), manifest.FileId);
        Assert.Equal(shaUpper.ToLowerInvariant(), manifest.PieceHashes[0]);

        // Kiểm tra ToJson xuất đúng tên trường v1
        string exportedJson = manifest.ToJson();
        Assert.Contains("\"displayName\"", exportedJson);
        Assert.Contains("\"fileSizeBytes\"", exportedJson);
        Assert.Contains("\"pieceSizeBytes\"", exportedJson);
        Assert.Contains("\"blockSizeBytes\"", exportedJson);

        // Mở Seed với manifest chuẩn hóa từ chữ hoa hoạt động bình thường
        using var seedStorage = new FileStorage(seedPath, manifest, isSeed: true);
        Assert.True(seedStorage.IsPieceVerified(0));
        var (readRes, blockData) = await seedStorage.ReadBlockAsync(0, 0);
        Assert.Equal(StorageStatus.Ok, readRes.Status);
        Assert.Equal(Encoding.ASCII.GetBytes("ABCD"), blockData);
    }

    [Fact]
    public async Task Seed_WithWrongFileSameSize_KeepsPiecesUnverified_And_RefusesToServeBlock()
    {
        string correctSeedPath = Path.Combine(_tempDir, "correct_seed.bin");
        string wrongSeedPath = Path.Combine(_tempDir, "wrong_seed_same_size.bin");

        await File.WriteAllBytesAsync(correctSeedPath, Encoding.ASCII.GetBytes("ABCDEFGH")); // 8 bytes
        await File.WriteAllBytesAsync(wrongSeedPath, Encoding.ASCII.GetBytes("12345678"));   // Cùng 8 bytes nhưng khác SHA-256

        var manifest = await FileManifest.CreateFromFileAsync(correctSeedPath, pieceLength: 8, blockLength: 4);

        // Mở Seed nhưng trỏ nhầm vào wrongSeedPath
        using var wrongSeedStorage = new FileStorage(wrongSeedPath, manifest, isSeed: true);

        // Piece phải ở trạng thái chưa Verified và không phục vụ block
        Assert.False(wrongSeedStorage.IsPieceVerified(0));

        var (readRes, data) = await wrongSeedStorage.ReadBlockAsync(0, 0);
        Assert.Equal(StorageStatus.HashMismatch, readRes.Status);
        Assert.Empty(data);
    }

    [Fact]
    public async Task RewritingVerifiedPiece_ClearsVerifiedFlagBeforeWrite_And_OnlyReenablesWhenHashMatches()
    {
        string seedPath = Path.Combine(_tempDir, "rewrite_seed.bin");
        string destPath = Path.Combine(_tempDir, "rewrite_dest.bin");
        await File.WriteAllBytesAsync(seedPath, Encoding.ASCII.GetBytes("ABCDEFGH"));

        var manifest = await FileManifest.CreateFromFileAsync(seedPath, pieceLength: 8, blockLength: 4);
        using var storage = new FileStorage(destPath, manifest, isSeed: false);

        // 1. Ghi đúng và Verify lần đầu -> IsPieceVerified = true
        await storage.WriteBlockAsync(0, 0, Encoding.ASCII.GetBytes("ABCD"));
        await storage.WriteBlockAsync(0, 1, Encoding.ASCII.GetBytes("EFGH"));
        Assert.Equal(StorageStatus.Verified, (await storage.VerifyPieceAsync(0)).Status);
        Assert.True(storage.IsPieceVerified(0));

        // 2. Ghi đè lại 1 block vào piece đã Verified -> Cờ Verified phải bị xóa ngay lập tức (trước khi Verify lại)
        await storage.WriteBlockAsync(0, 1, Encoding.ASCII.GetBytes("EFGZ")); // Ghi dữ liệu sai
        Assert.False(storage.IsPieceVerified(0));

        // 3. Nếu gọi VerifyPieceAsync với dữ liệu sai -> báo HashMismatch và giữ Verified = false
        Assert.Equal(StorageStatus.HashMismatch, (await storage.VerifyPieceAsync(0)).Status);
        Assert.False(storage.IsPieceVerified(0));

        // 4. Ghi lại đầy đủ dữ liệu đúng và Verify -> Bật lại Verified = true
        await storage.WriteBlockAsync(0, 0, Encoding.ASCII.GetBytes("ABCD"));
        await storage.WriteBlockAsync(0, 1, Encoding.ASCII.GetBytes("EFGH"));
        Assert.False(storage.IsPieceVerified(0)); // Trước khi gọi VerifyPieceAsync vẫn là false
        Assert.Equal(StorageStatus.Verified, (await storage.VerifyPieceAsync(0)).Status);
        Assert.True(storage.IsPieceVerified(0));
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
