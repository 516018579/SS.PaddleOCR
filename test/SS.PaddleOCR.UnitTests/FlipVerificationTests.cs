namespace SS.PaddleOCR.UnitTests;

/// <summary>
/// Guard contract for the flip-verification path: a classifier vote for 180°
/// is only acted on after the recognizer has seen both orientations and the
/// higher-scoring text wins. These tests pin the decision rules, not the model.
/// </summary>
public class FlipVerificationTests
{
    [Fact]
    public void DefaultsVerifyEveryFlipCandidate()
    {
        PaddleOcrOptions options = new();

        Assert.True(options.VerifyFlipsByRecognition);
        Assert.Equal(0f, options.FlipVerifyMinClassifierScore);
    }

    [Theory]
    [InlineData(1u, 0.55f, true)]
    [InlineData(1u, 0f, true)]
    [InlineData(0u, 0.99f, false)]
    public void ClassOneVerdictsAreVerified(uint label, float score, bool expected)
    {
        Assert.Equal(expected, PaddleOcrAll.ShouldVerifyFlip(new PaddleOcrOptions(), label, score));
    }

    [Fact]
    public void OptOutDisablesVerification()
    {
        PaddleOcrOptions options = new() { VerifyFlipsByRecognition = false };

        Assert.False(PaddleOcrAll.ShouldVerifyFlip(options, 1u, 0.99f));
    }

    [Fact]
    public void ScoreGateSkipsLowConfidenceCandidates()
    {
        PaddleOcrOptions options = new() { FlipVerifyMinClassifierScore = 0.95f };

        Assert.False(PaddleOcrAll.ShouldVerifyFlip(options, 1u, 0.9f));
        Assert.True(PaddleOcrAll.ShouldVerifyFlip(options, 1u, 0.95f));
    }
}