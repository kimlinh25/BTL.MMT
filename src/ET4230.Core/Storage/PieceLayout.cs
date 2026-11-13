namespace ET4230.Core.Storage;

/// <summary>
/// Chịu trách nhiệm thuần túy về mặt layout hình học: tính toán vị trí,
/// offset và độ dài của Piece và Block (Arithmetic F01).
/// Hoàn toàn độc lập với Socket và File I/O, không đọc/ghi và không hash SHA-256.
/// </summary>
public sealed class PieceLayout
{
    public long FileSize { get; }
    public int PieceLength { get; }
    public int BlockLength { get; }
    public int PieceCount { get; }

    public PieceLayout(long fileSize, int pieceLength, int blockLength)
    {
        // 1. Kiểm tra tham số không âm / dương
        if (fileSize < 0)
            throw new ArgumentOutOfRangeException(nameof(fileSize), "Kích thước tệp không được âm.");
        if (pieceLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(pieceLength), "Độ dài piece phải lớn hơn 0.");
        if (blockLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockLength), "Độ dài block phải lớn hơn 0.");

        // 2. Kiểm tra ràng buộc Giao thức V1
        if (pieceLength < blockLength)
            throw new ArgumentException("pieceLength phải lớn hơn hoặc bằng blockLength.", nameof(pieceLength));
        if (pieceLength % blockLength != 0)
            throw new ArgumentException("V1 yêu cầu pieceLength phải là bội số của blockLength.", nameof(pieceLength));

        FileSize = fileSize;
        PieceLength = pieceLength;
        BlockLength = blockLength;

        // 3. Xử lý riêng trường hợp tệp rỗng F = 0 trước khi tính toán
        if (fileSize == 0)
        {
            PieceCount = 0;
            return;
        }

        // 4. Tính toán pieceCount bằng phép chia tránh tràn số 64-bit
        long count = ((fileSize - 1) / pieceLength) + 1;
        if (count > int.MaxValue)
        {
            throw new OverflowException($"Số lượng piece ({count}) vượt quá giới hạn Int32.MaxValue.");
        }

        PieceCount = (int)count;
    }

    /// <summary>
    /// Lấy độ dài thực tế của Piece (đã cắt đúng biên với piece cuối lẻ).
    /// </summary>
    public int GetPieceLength(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= PieceCount)
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), $"Chỉ số piece {pieceIndex} ngoài phạm vi [0..{PieceCount - 1}].");

        // Ép kiểu (long) trước khi nhân để chống tràn số 32-bit
        long pieceOffset = (long)pieceIndex * PieceLength;
        return (int)Math.Min((long)PieceLength, FileSize - pieceOffset);
    }

    /// <summary>
    /// Lấy số lượng block trong một Piece.
    /// </summary>
    public int GetBlockCountInPiece(int pieceIndex)
    {
        int pLen = GetPieceLength(pieceIndex);
        return ((pLen - 1) / BlockLength) + 1;
    }

    /// <summary>
    /// Lấy tọa độ và độ dài của một Block trong tệp.
    /// </summary>
    public (long FileOffset, int OffsetInPiece, int Length) GetBlockRange(int pieceIndex, int blockIndex)
    {
        int pLen = GetPieceLength(pieceIndex);
        int blocksInThisPiece = GetBlockCountInPiece(pieceIndex);

        if (blockIndex < 0 || blockIndex >= blocksInThisPiece)
            throw new ArgumentOutOfRangeException(nameof(blockIndex), $"Chỉ số block {blockIndex} không hợp lệ cho piece {pieceIndex}.");

        int offsetInPiece = blockIndex * BlockLength;
        long fileOffset = (long)pieceIndex * PieceLength + offsetInPiece;
        int length = Math.Min(BlockLength, pLen - offsetInPiece);

        return (fileOffset, offsetInPiece, length);
    }
}
