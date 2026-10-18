using OqTish.Domain.Tenancy;

namespace OqTish.Application.Tenancy;

/// <summary>GET/POST/PUT /api/clinics/{clinicId}. Missing optional texts are "" (not null).</summary>
public sealed record ClinicProfileDto(
    Guid Id,
    string Name,
    string LegalName,
    string Description,
    string Phone,
    ClinicVerificationStatus VerificationStatus,
    string? StatusReason,
    DateTimeOffset CreatedAt);

public sealed record ClinicProfileCommand(string? Name, string? LegalName, string? Description, string? Phone);

/// <summary>Opening interval, branch-local <c>HH:mm</c>.</summary>
public sealed record OpeningHoursDto(int Weekday, string Open, string Close);

public sealed record BranchDto(
    Guid Id,
    string Name,
    string District,
    string Address,
    decimal? Latitude,
    decimal? Longitude,
    string Phone,
    string TimeZone,
    bool IsActive,
    IReadOnlyList<OpeningHoursDto> Hours);

public sealed record BranchCommand(
    string? Name,
    string? District,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string? Phone,
    string? TimeZone,
    bool? IsActive);

/// <summary>GET /api/clinics/{clinicId}/dashboard. <see cref="Collected"/> is null without <c>finance.report.view</c>.</summary>
public sealed record DashboardDto(
    string Date,
    Guid? BranchId,
    int Pending,
    int Confirmed,
    int Completed,
    int NoShow,
    OqTish.Domain.Common.Money? Collected);

internal static class TenancyMapping
{
    public static ClinicProfileDto ToDto(this Clinic c) =>
        new(c.Id, c.Name, c.LegalName ?? string.Empty, c.Description ?? string.Empty, c.Phone ?? string.Empty, c.VerificationStatus, c.StatusReason, c.CreatedAt);

    public static OpeningHoursDto ToDto(this BranchOpeningHours h) =>
        new(h.Weekday, Common.LocalFormats.Time(h.Opens), Common.LocalFormats.Time(h.Closes));

    public static BranchDto ToDto(this Branch b, IEnumerable<BranchOpeningHours> hours) =>
        new(b.Id, b.Name, b.District, b.Address, b.Latitude, b.Longitude, b.Phone ?? string.Empty, b.TimeZone, b.IsActive,
            [.. hours.Where(h => h.BranchId == b.Id).OrderBy(h => h.Weekday).ThenBy(h => h.Opens).Select(h => h.ToDto())]);
}
