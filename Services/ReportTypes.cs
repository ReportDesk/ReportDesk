namespace ReportSystem.Services;

public static class ReportTypes
{
    public const string Machine = "Machine";
    public const string NearMiss = "NearMiss";
    public const string Suggestion = "Suggestion";
    public const string Kaizen = "Kaizen";

    public static readonly string[] All =
    [
        Machine,
        NearMiss,
        Suggestion,
        Kaizen
    ];

    public static bool IsValid(string reportType)
    {
        return All.Contains(reportType, StringComparer.OrdinalIgnoreCase);
    }

    public static string Normalize(string reportType)
    {
        return All.First(x => string.Equals(x, reportType, StringComparison.OrdinalIgnoreCase));
    }
}
