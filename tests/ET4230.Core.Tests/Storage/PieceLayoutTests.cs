using ET4230.Core.Storage;
using Xunit;

namespace ET4230.Core.Tests.Storage;

public class PieceLayoutTests
{
    [Fact]
    public void Test_W01_Case_21_8_4()
    {
        var layout = new PieceLayout(21, 8, 4);
        Assert.Equal(3, layout.PieceCount);
        Assert.Equal(5, layout.GetPieceLength(2));

        var bTail = layout.GetBlockRange(2, 1);
        Assert.Equal(20, bTail.FileOffset);
        Assert.Equal(4, bTail.OffsetInPiece);
        Assert.Equal(1, bTail.Length);
    }

    [Fact]
    public void Test_New_Input_Case_19_8_4()
    {
        var layout = new PieceLayout(19, 8, 4);
        Assert.Equal(3, layout.PieceCount);
        Assert.Equal(3, layout.GetPieceLength(2));
        Assert.Equal(1, layout.GetBlockCountInPiece(2));

        var bTail = layout.GetBlockRange(2, 0);
        Assert.Equal(16, bTail.FileOffset);
        Assert.Equal(3, bTail.Length);
    }

    [Fact]
    public void Test_EmptyFile_0_Bytes_ShouldHaveZeroPieces()
    {
        var layout = new PieceLayout(0, 8, 4);
        Assert.Equal(0, layout.PieceCount);

        // Đảm bảo không tồn tại piece 0 hợp lệ
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetPieceLength(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetBlockRange(0, 0));
    }

    [Fact]
    public void Test_Protocol_V1_Constraints_ThrowsArgumentException()
    {
        // P < B
        Assert.Throws<ArgumentException>(() => new PieceLayout(100, 4, 8));

        // P % B != 0
        Assert.Throws<ArgumentException>(() => new PieceLayout(100, 10, 4));
    }

    [Fact]
    public void Test_PieceCount_Overflow_ThrowsOverflowException()
    {
        // FileSize rất lớn, pieceLength = 1 khiến số piece vượt int.MaxValue
        long hugeFileSize = (long)int.MaxValue + 100L;
        Assert.Throws<OverflowException>(() => new PieceLayout(hugeFileSize, 1, 1));
    }

    [Fact]
    public void Test_Divisible_Case_16_8_4()
    {
        var layout = new PieceLayout(16, 8, 4);
        Assert.Equal(2, layout.PieceCount);
        Assert.Equal(8, layout.GetPieceLength(0));
        Assert.Equal(8, layout.GetPieceLength(1));
    }
}
