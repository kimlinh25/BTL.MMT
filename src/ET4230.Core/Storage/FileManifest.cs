using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ET4230.Core.Storage;

public sealed class FileManifest
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("fileId")]
    public string FileId { get; init; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("fileSize")]
    public long FileSize { get; init; }

    [JsonPropertyName("pieceLength")]
    public int PieceLength { get; init; }

    [JsonPropertyName("blockLength")]
    public int BlockLength { get; init; }

    [JsonPropertyName("pieceHashes")]
    public IReadOnlyList<string> PieceHashes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Tạo đối tượng PieceLayout từ thông số của Manifest sau khi đã validate.
    /// </summary>
    public PieceLayout CreateLayout()
    {
        Validate();
        return new PieceLayout(FileSize, PieceLength, BlockLength);
    }

    /// <summary>
    /// Kiểm tra tính hợp lệ của Manifest v1 (fileSize, piece/block size, fileId, pieceHashes).
    /// </summary>
    public void Validate()
    {
        if (Version != 1)
            throw new InvalidDataException($"Phiên bản Manifest không hỗ trợ: {Version} (yêu cầu v1).");

        if (string.IsNullOrWhiteSpace(FileId) || !IsValidSha256Hex(FileId))
            throw new InvalidDataException("FileId không hợp lệ (phải là chuỗi SHA-256 hex 64 ký tự).");

        // Dùng chính PieceLayout để kiểm tra ràng buộc fileSize >= 0, P >= B, P % B == 0
        PieceLayout layout;
        try
        {
            layout = new PieceLayout(FileSize, PieceLength, BlockLength);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Thông số kích thước trong Manifest không hợp lệ: {ex.Message}", ex);
        }

        if (PieceHashes is null)
            throw new InvalidDataException("Danh sách PieceHashes không được null.");

        if (PieceHashes.Count != layout.PieceCount)
            throw new InvalidDataException(
                $"Số lượng pieceHashes ({PieceHashes.Count}) không khớp với PieceCount ({layout.PieceCount}) từ PieceLayout.");

        for (int i = 0; i < PieceHashes.Count; i++)
        {
            if (!IsValidSha256Hex(PieceHashes[i]))
                throw new InvalidDataException($"PieceHash tại vị trí {i} không phải là SHA-256 hex 64 ký tự hợp lệ.");
        }
    }

    /// <summary>
    /// Tạo Manifest v1 từ một tệp nguồn có sẵn trên đĩa (phía Seed).
    /// </summary>
    public static async Task<FileManifest> CreateFromFileAsync(
        string filePath,
        int pieceLength,
        int blockLength,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Đường dẫn tệp không được rỗng.", nameof(filePath));

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Không tìm thấy tệp nguồn để tạo Manifest.", filePath);

        long fileSize = fileInfo.Length;
        var layout = new PieceLayout(fileSize, pieceLength, blockLength);
        var pieceHashes = new List<string>(layout.PieceCount);

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var fullFileHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        for (int p = 0; p < layout.PieceCount; p++)
        {
            int pLen = layout.GetPieceLength(p);
            byte[] buffer = new byte[pLen];
            int totalRead = 0;

            while (totalRead < pLen)
            {
                int read = await fileStream.ReadAsync(buffer.AsMemory(totalRead, pLen - totalRead), ct);
                if (read == 0)
                    throw new EndOfStreamException($"Tệp bị cắt cụt bất ngờ khi đọc piece {p}.");
                totalRead += read;
            }

            fullFileHasher.AppendData(buffer);
            byte[] pieceHashBytes = SHA256.HashData(buffer);
            pieceHashes.Add(Convert.ToHexString(pieceHashBytes).ToLowerInvariant());
        }

        byte[] fullHashBytes = fullFileHasher.GetHashAndReset();
        string fileId = Convert.ToHexString(fullHashBytes).ToLowerInvariant();

        var manifest = new FileManifest
        {
            Version = 1,
            FileId = fileId,
            FileName = fileInfo.Name,
            FileSize = fileSize,
            PieceLength = pieceLength,
            BlockLength = blockLength,
            PieceHashes = pieceHashes
        };

        manifest.Validate();
        return manifest;
    }

    public string ToJson()
    {
        Validate();
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }

    public static FileManifest FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Nội dung JSON của Manifest rỗng.");

        var manifest = JsonSerializer.Deserialize<FileManifest>(json)
            ?? throw new InvalidDataException("Không thể đọc dữ liệu Manifest từ JSON.");

        manifest.Validate();
        return manifest;
    }

    private static bool IsValidSha256Hex(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != 64)
            return false;

        foreach (char c in hex)
        {
            bool isHex = (c >= '0' && c <= '9') ||
                         (c >= 'a' && c <= 'f') ||
                         (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }
}
