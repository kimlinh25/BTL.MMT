using ET4230.Core.Storage;

namespace ET4230.Core.Transport;

/// <summary>Ánh xạ yêu cầu block v1 vào FileStorage do caller sở hữu suốt phiên seed.</summary>
public sealed class FileSeedSource(FileStorage storage) : ISeedSource
{
    public string FileId => storage.Manifest.FileId;
    public int PieceCount => storage.Layout.PieceCount;

    public bool IsVerified(int pieceIndex) => storage.IsPieceVerified(pieceIndex);
    public bool[] SnapshotVerifiedPieces() => storage.SnapshotVerifiedPieces();

    public async ValueTask<BlockReadResult> ReadBlockAsync(BlockRequest request, CancellationToken ct)
    {
        PieceLayout layout = storage.Layout;
        if (request.PieceIndex >= (uint)layout.PieceCount || request.Length == 0 ||
            request.OffsetInPiece % (uint)layout.BlockLength != 0)
            return new(BlockReadStatus.InvalidRange, default);

        int pieceIndex = (int)request.PieceIndex;
        uint blockIndex = request.OffsetInPiece / (uint)layout.BlockLength;
        if (blockIndex >= layout.GetBlockCountInPiece(pieceIndex))
            return new(BlockReadStatus.InvalidRange, default);

        var range = layout.GetBlockRange(pieceIndex, (int)blockIndex);
        if (request.Length != (uint)range.Length)
            return new(BlockReadStatus.InvalidRange, default);

        var (result, data) = await storage.ReadBlockAsync(pieceIndex, (int)blockIndex, ct);
        return result.Status switch
        {
            StorageStatus.Ok => new(BlockReadStatus.Available, data),
            StorageStatus.HashMismatch => new(BlockReadStatus.PieceUnavailable, default),
            StorageStatus.InvalidRange => new(BlockReadStatus.InvalidRange, default),
            _ => throw new IOException(result.ErrorMessage ?? result.Status.ToString())
        };
    }
}
