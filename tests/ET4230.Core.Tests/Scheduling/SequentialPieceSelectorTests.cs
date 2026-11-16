using ET4230.Core.Scheduling;

namespace ET4230.Core.Tests.Scheduling;

public sealed class SequentialPieceSelectorTests
{
    [Fact]
    public void UsesCurrentSourcesAndCallerStateWithoutChangingInputs()
    {
        bool[] a = [true, true, true, true, true];
        bool[] b = [true, false, true, false, false];
        bool[] verified = [true, false, false, false, false];
        bool[] inFlight = [false, true, false, false, false];

        Assert.Equal(2, SequentialPieceSelector.SelectSequentialPiece(5, [a, b], verified, inFlight));
        Assert.Equal(2, SequentialPieceSelector.SelectSequentialPiece(5, [a, b], verified, inFlight));
        Assert.Equal(2, SequentialPieceSelector.SelectSequentialPiece(5, [b], verified, inFlight));
        Assert.Equal(new[] { true, true, true, true, true }, a);
        Assert.Equal(new[] { true, false, true, false, false }, b);
        Assert.Equal(new[] { true, false, false, false, false }, verified);
        Assert.Equal(new[] { false, true, false, false, false }, inFlight);

        inFlight[2] = true;
        Assert.Equal(3, SequentialPieceSelector.SelectSequentialPiece(5, [a, b], verified, inFlight));
        Assert.Null(SequentialPieceSelector.SelectSequentialPiece(5, [b], verified, inFlight));
    }

    [Fact]
    public void DistinguishesPieceZeroFromNoCandidate()
    {
        Assert.Equal(0, SequentialPieceSelector.SelectSequentialPiece(1, [new[] { true }], [false], [false]));
        Assert.Null(SequentialPieceSelector.SelectSequentialPiece(1, [], [false], [false]));
        Assert.Null(SequentialPieceSelector.SelectSequentialPiece(1, [new[] { true }], [true], [false]));
        Assert.Null(SequentialPieceSelector.SelectSequentialPiece(0, [], [], []));
    }

    [Fact]
    public void RejectsInconsistentInputBeforeSelecting()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SequentialPieceSelector.SelectSequentialPiece(-1, [], [], []));
        Assert.Throws<ArgumentNullException>(() =>
            SequentialPieceSelector.SelectSequentialPiece(1, null!, [false], [false]));
        Assert.Throws<ArgumentException>(() =>
            SequentialPieceSelector.SelectSequentialPiece(1, [], [], [false]));
        Assert.Throws<ArgumentException>(() =>
            SequentialPieceSelector.SelectSequentialPiece(1, [], [false], []));
        Assert.Throws<ArgumentException>(() =>
            SequentialPieceSelector.SelectSequentialPiece(1, [new[] { true }, Array.Empty<bool>()], [false], [false]));
    }
}
