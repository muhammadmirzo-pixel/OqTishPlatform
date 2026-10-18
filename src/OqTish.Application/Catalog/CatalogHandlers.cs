using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Catalog;
using OqTish.Domain.Common;

namespace OqTish.Application.Catalog;

public sealed record BranchOfferDto(Guid BranchId, Money Price, PriceKind PriceKind, int DurationMinutes, bool IsActive);

/// <summary>A clinic service with its per-branch offers (offers limited to the caller's branches).</summary>
public sealed record ServiceDto(Guid Id, string Name, string Category, bool IsActive, IReadOnlyList<BranchOfferDto> Offers);

public sealed record ServiceCommand(string? Name, string? Category, bool? IsActive);

public sealed record BranchOfferCommand(Money? Price, PriceKind? PriceKind, int? DurationMinutes, bool? IsActive);

/// <summary>
/// Clinic services and branch offers (docs/API.md §4).
/// <list type="bullet">
/// <item>GET …/services — any membership. All clinic services; <c>offers</c> only for branches in the caller's scope.</item>
/// <item>POST/PUT …/services[/{serviceId}] — <c>catalog.manage</c> clinic-wide (a service belongs to the whole clinic).</item>
/// <item>PUT …/branches/{branchId}/services/{serviceId} — <c>catalog.manage</c> on that branch; creates or updates the offer.</item>
/// </list>
/// Side effects: audit <c>catalog.service.created</c> / <c>catalog.service.updated</c> / <c>catalog.offer.updated</c>.
/// </summary>
public sealed class CatalogHandlers(IOqTishDb db, IAccessService access, IAuditLog audit, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<ServiceDto>>> ListAsync(Guid clinicId, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        var check = scope.CheckMember(clinicId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var branchIds = scope.MembershipIn(clinicId)!.AccessibleBranchIds;
        var services = await db.Services.AsNoTracking().Where(s => s.ClinicId == clinicId).OrderBy(s => s.Name).ThenBy(s => s.Id).ToListAsync(ct);
        var offers = await db.BranchServices.AsNoTracking().Where(o => o.ClinicId == clinicId && branchIds.Contains(o.BranchId)).ToListAsync(ct);
        return services.Select(s => ToDto(s, offers)).ToList();
    }

    public async Task<Result<ServiceDto>> CreateAsync(Guid clinicId, ServiceCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var check = (await access.GetScopeAsync(ct)).CheckClinicWide(Permissions.CatalogManage, clinicId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        if (Validate(command) is { } error)
        {
            return error;
        }

        var service = Service.Create(clinicId, command.Name!, command.Category!, command.IsActive ?? true, clock.GetUtcNow());
        db.Services.Add(service);
        audit.Record(new AuditEvent("catalog.service.created", clinicId, TargetType: "service", TargetId: service.Id.ToString()));
        await db.SaveChangesAsync(ct);
        return ToDto(service, []);
    }

    public async Task<Result<ServiceDto>> UpdateAsync(Guid clinicId, Guid serviceId, ServiceCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == serviceId && s.ClinicId == clinicId, ct);
        if (service is null)
        {
            return CommonErrors.NotFound;
        }

        var scope = await access.GetScopeAsync(ct);
        var check = scope.CheckClinicWide(Permissions.CatalogManage, service.ClinicId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        if (Validate(command) is { } error)
        {
            return error;
        }

        service.Update(command.Name!, command.Category!, command.IsActive ?? service.IsActive, clock.GetUtcNow());
        audit.Record(new AuditEvent("catalog.service.updated", clinicId, TargetType: "service", TargetId: service.Id.ToString(), Details: new { service.IsActive }));
        await db.SaveChangesAsync(ct);
        var offers = await db.BranchServices.AsNoTracking().Where(o => o.ServiceId == service.Id).ToListAsync(ct);
        return ToDto(service, offers);
    }

    public async Task<Result> SetOfferAsync(Guid clinicId, Guid branchId, Guid serviceId, BranchOfferCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId && b.ClinicId == clinicId, ct);
        var service = await db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceId && s.ClinicId == clinicId, ct);
        if (branch is null || service is null)
        {
            return CommonErrors.NotFound;
        }

        var check = (await access.GetScopeAsync(ct)).Check(Permissions.CatalogManage, branch.ClinicId, branch.Id);
        if (!check.IsSuccess)
        {
            return check;
        }

        var errors = new FieldErrors();
        if (command.Price is not { } price)
        {
            errors.Add("price", "Required.");
        }
        else if (price.IsNegative)
        {
            errors.Add("price", "Must be 0 or greater.");
        }

        if (command.PriceKind is null)
        {
            errors.Add("priceKind", "Required (Fixed or From).");
        }

        if (command.DurationMinutes is not (>= BranchService.MinDurationMinutes and <= BranchService.MaxDurationMinutes))
        {
            errors.Add("durationMinutes", $"Must be {BranchService.MinDurationMinutes}..{BranchService.MaxDurationMinutes} minutes.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var now = clock.GetUtcNow();
        var offer = await db.BranchServices.FirstOrDefaultAsync(o => o.BranchId == branch.Id && o.ServiceId == service.Id, ct);
        if (offer is null)
        {
            offer = BranchService.Create(branch.ClinicId, branch.Id, service.Id, command.Price!.Value, command.PriceKind!.Value, command.DurationMinutes!.Value, command.IsActive ?? true, now);
            db.BranchServices.Add(offer);
        }
        else
        {
            offer.Update(command.Price!.Value, command.PriceKind!.Value, command.DurationMinutes!.Value, command.IsActive ?? offer.IsActive, now);
        }

        audit.Record(new AuditEvent("catalog.offer.updated", branch.ClinicId, branch.Id, "service", service.Id.ToString(),
            new { price = offer.PriceMoney.ToAmountString(), priceKind = offer.PriceKind.ToString(), offer.DurationMinutes, offer.IsActive }));
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    private static Error? Validate(ServiceCommand command)
    {
        var errors = new FieldErrors();
        errors.Text(command.Name, "name", Service.MaxNameLength, required: true);
        if (!ServiceCategories.IsKnown(command.Category))
        {
            errors.Add("category", "Unknown category; use a key from GET /api/public/facets.");
        }

        return errors.HasErrors ? errors.ToError() : null;
    }

    private static ServiceDto ToDto(Service s, IEnumerable<BranchService> offers) =>
        new(s.Id, s.Name, s.Category, s.IsActive,
            [.. offers.Where(o => o.ServiceId == s.Id).OrderBy(o => o.BranchId)
                .Select(o => new BranchOfferDto(o.BranchId, o.PriceMoney, o.PriceKind, o.DurationMinutes, o.IsActive))]);
}
