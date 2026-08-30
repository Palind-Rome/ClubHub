using ClubHub.Api.Controllers;

namespace ClubHub.Api.Tests;

public sealed class EvaluationTermNormalizationTests
{
    [Theory]
    [InlineData("2026 学年春季学期", "2025-2026学年春季")]
    [InlineData("2026学年秋季学期", "2026-2027学年秋季")]
    [InlineData(" 2026  学年  春季 ", "2025-2026学年春季")]
    public void NormalizesLegacySemesterNamesToCanonicalWindows(string input, string expected)
    {
        Assert.Equal(expected, ClubsController.NormalizeSemesterEvaluationTermName(input));
    }

    [Theory]
    [InlineData("2025-2026学年春季")]
    [InlineData("2026-2027学年秋季")]
    [InlineData("2027春季")]
    public void KeepsCanonicalOrShortSemesterNamesStable(string input)
    {
        Assert.Equal(input, ClubsController.NormalizeSemesterEvaluationTermName(input));
    }
}
