namespace ET4230.Core.Scheduling;

public static class SequentialPieceSelector
{
    /// <summary>
    /// Chọn piece có index nhỏ nhất đủ điều kiện từ danh sách nguồn khả dụng.
    /// Caller sở hữu các collection và cập nhật InFlight trước lần chọn tiếp theo.
    /// </summary>
    public static int? SelectSequentialPiece(
        int pieceCount,
        IReadOnlyList<IReadOnlyList<bool>> availability,
        IReadOnlyList<bool> verified,
        IReadOnlyList<bool> inFlight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pieceCount);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(inFlight);

        if (verified.Count != pieceCount)
            throw new ArgumentException("Verified phải có đúng số piece.", nameof(verified));
        if (inFlight.Count != pieceCount)
            throw new ArgumentException("InFlight phải có đúng số piece.", nameof(inFlight));

        foreach (var source in availability)
        {
            if (source is null || source.Count != pieceCount)
                throw new ArgumentException("Mỗi nguồn phải có bảng availability đúng số piece.", nameof(availability));
        }

        for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
        {
            if (verified[pieceIndex] || inFlight[pieceIndex])
                continue;

            foreach (var source in availability)
            {
                if (source[pieceIndex])
                    return pieceIndex;
            }
        }

        return null;
    }
}
