using ClubHub.Api.Services;

namespace ClubHub.Api.Tests;

public sealed class AcademicTermHelperTests
{
    [Theory]
    [InlineData(2026, 6, 30, 2025)]
    [InlineData(2026, 7, 1, 2026)]
    [InlineData(2027, 1, 15, 2026)]
    public void FromDateUsesJulyAsAcademicYearBoundary(int year, int month, int day, int startYear)
    {
        var term = AcademicTermHelper.FromDate(new DateTime(year, month, day, 23, 59, 59));

        Assert.Equal($"{startYear}-{startYear + 1}学年", term.Label);
        Assert.Equal(new DateTime(startYear, 7, 1), term.Start);
        Assert.Equal(new DateTime(startYear + 1, 6, 30), term.End);
    }
}
