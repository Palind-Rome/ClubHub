namespace ClubHub.Api.Services;

public sealed record AcademicTerm(string Label, DateTime Start, DateTime End);

public static class AcademicTermHelper
{
    public static AcademicTerm FromDate(DateTime date)
    {
        var businessDate = date.Date;
        var startYear = businessDate.Month >= 7 ? businessDate.Year : businessDate.Year - 1;
        return FromStartYear(startYear);
    }

    public static AcademicTerm FromStartYear(int startYear) =>
        new(
            $"{startYear}-{startYear + 1}学年",
            new DateTime(startYear, 7, 1),
            new DateTime(startYear + 1, 6, 30));
}
