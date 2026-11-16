using System.Collections;
using System.Security.Cryptography;

namespace ET4230.Core.Storage;

public enum StorageStatus
{
    Ok,
    Verified,
    IncompletePiece,
    HashMismatch,
    InvalidRange,
    IoError
}

public sealed record StorageResult(
    StorageStatus Status,
    string? ErrorMessage = null,
    string? ExpectedHash = null,
    string? ActualHash = null)
{
    public bool IsSuccess => Status is StorageStatus.Ok or StorageStatus.Verified;
}

public sealed class FileStorage : IDisposable
{
    private readonly string _targetFilePath;
    private readonly FileManifest _manifest;
    private readonly PieceLayout _layout;
    private readonly FileStream _stream;
    private readonly SemaphoreSlim _writerLock = new(1, 1);

    private readonly BitArray[] _writtenBlocksPerPiece;
    private readonly BitArray _verifiedPieces;
    private bool _disposed;

    public string TargetFilePath => _targetFilePath;
    public PieceLayout Layout => _layout;
    public FileManifest Manifest => _manifest;

    /// <summary>
    /// Mở hoặc tạo tệp đích theo đường dẫn do người chạy chỉ định.
    /// Khi mở ở chế độ Seed (isSeed = true), tự động kiểm tra SHA-256 từng piece trên đĩa:
    /// chỉ những piece khớp SHA-256 với Manifest mới được đánh dấu Verified và được phép phục vụ block.
    /// </summary>
    public FileStorage(string targetFilePath, FileManifest manifest, bool isSeed = false)
    {
        if (string.IsNullOrWhiteSpace(targetFilePath))
            throw new ArgumentException("Đường dẫn tệp đích không được để trống.", nameof(targetFilePath));

        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _manifest.Validate();
        _layout = _manifest.CreateLayout();
        _targetFilePath = Path.GetFullPath(targetFilePath);

        string? dir = Path.GetDirectoryName(_targetFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Khởi tạo tất cả các cờ ở trạng thái false (chỉ bật Verified sau khi đã kiểm tra hash thật)
        _writtenBlocksPerPiece = new BitArray[_layout.PieceCount];
        for (int p = 0; p < _layout.PieceCount; p++)
        {
            int blockCount = _layout.GetBlockCountInPiece(p);
            _writtenBlocksPerPiece[p] = new BitArray(blockCount, false);
        }

        _verifiedPieces = new BitArray(_layout.PieceCount, false);

        var fileMode = isSeed ? FileMode.Open : FileMode.OpenOrCreate;
        var fileAccess = isSeed ? FileAccess.Read : FileAccess.ReadWrite;

        _stream = new FileStream(_targetFilePath, fileMode, fileAccess, FileShare.Read); ;

        if (isSeed)
        {
            VerifyExistingSeedPieces();
        }
        else if (_stream.Length != _layout.FileSize)
        {
            _stream.SetLength(_layout.FileSize);
        }
    }

    /// <summary>
    /// Kiểm tra dữ liệu thực tế của tệp Seed khi mở.
    /// Nếu chọn nhầm tệp (dù cùng kích thước nhưng khác SHA-256), piece sẽ giữ ở trạng thái chưa Verified (false).
    /// </summary>
    private void VerifyExistingSeedPieces()
    {
        if (_stream.Length != _layout.FileSize)
        {
            return;
        }

        for (int p = 0; p < _layout.PieceCount; p++)
        {
            int pLen = _layout.GetPieceLength(p);
            long pieceOffset = (long)p * _layout.PieceLength;
            byte[] pieceBuffer = new byte[pLen];

            _stream.Seek(pieceOffset, SeekOrigin.Begin);
            int totalRead = 0;
            while (totalRead < pLen)
            {
                int read = _stream.Read(pieceBuffer, totalRead, pLen - totalRead);
                if (read == 0) break;
                totalRead += read;
            }

            if (totalRead != pLen)
                continue;

            string actualHash = Convert.ToHexString(SHA256.HashData(pieceBuffer)).ToLowerInvariant();
            string expectedHash = _manifest.PieceHashes[p];

            if (string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                _writtenBlocksPerPiece[p].SetAll(true);
                _verifiedPieces[p] = true;
            }
        }
    }

    /// <summary>
    /// API đọc block cho phía Seed (dùng PieceLayout làm nguồn tính range).
    /// Chỉ phục vụ block khi piece đó đã đạt trạng thái Verified.
    /// </summary>
    public async Task<(StorageResult Result, byte[] Data)> ReadBlockAsync(
        int pieceIndex,
        int blockIndex,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        (long fileOffset, int offsetInPiece, int length) range;
        try
        {
            range = _layout.GetBlockRange(pieceIndex, blockIndex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return (new StorageResult(StorageStatus.InvalidRange, ex.Message), Array.Empty<byte>());
        }

        await _writerLock.WaitAsync(ct);
        try
        {
            // Không phục vụ block nếu piece hiện tại chưa được xác minh SHA-256 (Verified)
            if (!_verifiedPieces[pieceIndex])
            {
                return (new StorageResult(
                    StorageStatus.HashMismatch,
                    $"Piece {pieceIndex} chưa ở trạng thái Verified (chưa xác minh hoặc sai SHA-256), từ chối phục vụ block."),
                    Array.Empty<byte>());
            }

            byte[] buffer = new byte[range.length];
            _stream.Seek(range.fileOffset, SeekOrigin.Begin);

            int totalRead = 0;
            while (totalRead < range.length)
            {
                int read = await _stream.ReadAsync(buffer.AsMemory(totalRead, range.length - totalRead), ct);
                if (read == 0)
                {
                    return (new StorageResult(
                        StorageStatus.IoError,
                        $"Đọc thiếu dữ liệu tại offset {range.fileOffset} (đọc được {totalRead}/{range.length} byte)."),
                        Array.Empty<byte>());
                }
                totalRead += read;
            }

            return (new StorageResult(StorageStatus.Ok), buffer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (new StorageResult(StorageStatus.IoError, ex.Message), Array.Empty<byte>());
        }
        finally
        {
            _writerLock.Release();
        }
    }

    /// <summary>
    /// API ghi block tuần tự vào tệp đích theo fileOffset.
    /// Nếu một piece đã Verified bị ghi lại, xóa ngay cờ xác minh trước khi ghi và chỉ bật lại sau khi VerifyPieceAsync khớp hash.
    /// </summary>
    public async Task<StorageResult> WriteBlockAsync(
        int pieceIndex,
        int blockIndex,
        ReadOnlyMemory<byte> data,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        (long fileOffset, int offsetInPiece, int expectedLength) range;
        try
        {
            range = _layout.GetBlockRange(pieceIndex, blockIndex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return new StorageResult(StorageStatus.InvalidRange, ex.Message);
        }

        if (data.Length != range.expectedLength)
        {
            return new StorageResult(
                StorageStatus.InvalidRange,
                $"Độ dài dữ liệu block ({data.Length}) không khớp với độ dài hợp lệ ({range.expectedLength}) tại piece {pieceIndex}, block {blockIndex}.");
        }

        await _writerLock.WaitAsync(ct);
        try
        {
            // Xóa cờ xác minh của piece trước khi ghi đè; chỉ bật lại khi gọi VerifyPieceAsync và hash khớp
            _verifiedPieces[pieceIndex] = false;
            _writtenBlocksPerPiece[pieceIndex][blockIndex] = false;

            _stream.Seek(range.fileOffset, SeekOrigin.Begin);
            await _stream.WriteAsync(data, ct);
            await _stream.FlushAsync(ct);

            _writtenBlocksPerPiece[pieceIndex][blockIndex] = true;
            return new StorageResult(StorageStatus.Ok);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StorageResult(StorageStatus.IoError, ex.Message);
        }
        finally
        {
            _writerLock.Release();
        }
    }

    /// <summary>
    /// Kiểm tra xem một piece đã được ghi đủ tất cả các block xuống đĩa chưa.
    /// </summary>
    public bool IsPieceFullyWritten(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= _layout.PieceCount)
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));

        var blocks = _writtenBlocksPerPiece[pieceIndex];
        for (int b = 0; b < blocks.Count; b++)
        {
            if (!blocks[b]) return false;
        }
        return true;
    }

    public bool IsPieceVerified(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= _layout.PieceCount)
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));
        return _verifiedPieces[pieceIndex];
    }

    /// <summary>
    /// Khi ghi đủ piece, đọc lại đúng vùng đã ghi trên đĩa, tính SHA-256 và đối chiếu với Manifest.
    /// </summary>
    public async Task<StorageResult> VerifyPieceAsync(int pieceIndex, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        int pLen;
        try
        {
            pLen = _layout.GetPieceLength(pieceIndex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return new StorageResult(StorageStatus.InvalidRange, ex.Message);
        }

        await _writerLock.WaitAsync(ct);
        try
        {
            _verifiedPieces[pieceIndex] = false;

            if (!IsPieceFullyWritten(pieceIndex))
            {
                return new StorageResult(
                    StorageStatus.IncompletePiece,
                    $"Piece {pieceIndex} chưa được ghi đủ tất cả các block xuống đĩa.");
            }

            long pieceOffset = (long)pieceIndex * _layout.PieceLength;
            byte[] pieceBuffer = new byte[pLen];

            _stream.Seek(pieceOffset, SeekOrigin.Begin);
            int totalRead = 0;
            while (totalRead < pLen)
            {
                int read = await _stream.ReadAsync(pieceBuffer.AsMemory(totalRead, pLen - totalRead), ct);
                if (read == 0)
                {
                    return new StorageResult(
                        StorageStatus.IoError,
                        $"Không đọc đủ {pLen} byte của piece {pieceIndex} từ đĩa để băm.");
                }
                totalRead += read;
            }

            string actualHash = Convert.ToHexString(SHA256.HashData(pieceBuffer)).ToLowerInvariant();
            string expectedHash = _manifest.PieceHashes[pieceIndex];

            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                _verifiedPieces[pieceIndex] = false;
                _writtenBlocksPerPiece[pieceIndex].SetAll(false);

                return new StorageResult(
                    StorageStatus.HashMismatch,
                    $"SHA-256 của piece {pieceIndex} không khớp với Manifest.",
                    ExpectedHash: expectedHash,
                    ActualHash: actualHash);
            }

            _verifiedPieces[pieceIndex] = true;
            return new StorageResult(StorageStatus.Verified, ExpectedHash: expectedHash, ActualHash: actualHash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StorageResult(StorageStatus.IoError, ex.Message);
        }
        finally
        {
            _writerLock.Release();
        }
    }

    /// <summary>
    /// Kiểm tra toàn bộ tệp khi tải xong (bao gồm cả trường hợp tệp rỗng F = 0).
    /// </summary>
    public async Task<StorageResult> VerifyFullFileAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        for (int p = 0; p < _layout.PieceCount; p++)
        {
            if (!_verifiedPieces[p])
            {
                var pieceRes = await VerifyPieceAsync(p, ct);
                if (pieceRes.Status != StorageStatus.Verified)
                {
                    return pieceRes;
                }
            }
        }

        await _writerLock.WaitAsync(ct);
        try
        {
            await _stream.FlushAsync(ct);

            if (_stream.Length != _layout.FileSize)
            {
                return new StorageResult(
                    StorageStatus.IoError,
                    $"Kích thước tệp trên đĩa ({_stream.Length}) khác với fileSizeBytes trong Manifest ({_layout.FileSize}).");
            }

            _stream.Seek(0, SeekOrigin.Begin);
            byte[] fullHashBytes = await SHA256.HashDataAsync(_stream, ct);
            string actualFileId = Convert.ToHexString(fullHashBytes).ToLowerInvariant();
            string expectedFileId = _manifest.FileId;

            if (!string.Equals(actualFileId, expectedFileId, StringComparison.OrdinalIgnoreCase))
            {
                return new StorageResult(
                    StorageStatus.HashMismatch,
                    "Mã băm SHA-256 toàn tệp không khớp với FileId trong Manifest.",
                    ExpectedHash: expectedFileId,
                    ActualHash: actualFileId);
            }

            return new StorageResult(StorageStatus.Verified, ExpectedHash: expectedFileId, ActualHash: actualFileId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StorageResult(StorageStatus.IoError, ex.Message);
        }
        finally
        {
            _writerLock.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileStorage));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
        _writerLock.Dispose();
    }
}
