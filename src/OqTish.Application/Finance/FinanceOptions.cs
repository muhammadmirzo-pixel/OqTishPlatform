namespace OqTish.Application.Finance;

/// <summary>Configuration section <c>Finance</c>.</summary>
public sealed class FinanceOptions
{
    public const string SectionName = "Finance";

    /// <summary>Branch-local wall-clock time (Asia/Tashkent) of the nightly summary rebuild + reconciliation, HH:mm.</summary>
    public string SummaryRunAtLocalTime { get; set; } = "02:00";

    /// <summary>Trailing window (days, including today) recomputed by the nightly job.</summary>
    public int SummaryWindowDays { get; set; } = 35;

    /// <summary>Longest range of <c>GET …/reports/daily</c>.</summary>
    public int DailyReportMaxDays { get; set; } = 92;
}
