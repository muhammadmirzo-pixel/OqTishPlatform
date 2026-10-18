using OqTish.Api.Common;
using OqTish.Application.Audit;
using OqTish.Application.Catalog;
using OqTish.Application.Common;
using OqTish.Application.Scheduling;
using OqTish.Application.Staff;
using OqTish.Application.Tenancy;

namespace OqTish.Api.Endpoints;

/// <summary>/api/clinics — clinic console (docs/API.md §4). Handlers do every authorization check.</summary>
public static class ClinicEndpoints
{
    public static IEndpointRouteBuilder MapClinicEndpoints(this IEndpointRouteBuilder app)
    {
        var clinics = app.MapGroup("/api/clinics").WithTags("Clinic console").RequireAuthorization();
        var clinic = clinics.MapGroup("/{clinicId:guid}");

        clinics.MapPost("", async (ClinicProfileCommand body, RegisterClinicHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttp(http, r => TypedResults.Created($"/api/clinics/{r.Id}", r)))
            .WithName("RegisterClinic").WithSummary("Register a clinic (Pending verification); the caller becomes ClinicOwner")
            .Produces<ClinicProfileDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(422);

        clinic.MapGet("", async (Guid clinicId, ClinicProfileHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.GetAsync(clinicId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("GetClinic").WithSummary("Clinic profile and verification status (any membership)")
            .Produces<ClinicProfileDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPut("", async (Guid clinicId, ClinicProfileCommand body, ClinicProfileHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.UpdateAsync(clinicId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("UpdateClinic").WithSummary("Edit the clinic profile (clinic.profile.manage)")
            .Produces<ClinicProfileDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapGet("/dashboard", async (Guid clinicId, Guid? branchId, ClinicDashboardQuery query, HttpContext http, CancellationToken ct) =>
                (await query.HandleAsync(clinicId, branchId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ClinicDashboard").WithSummary("Today's counts; collected sum only with finance.report.view")
            .Produces<DashboardDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapGet("/audit", async (Guid clinicId, int? page, int? pageSize, AuditQueries query, HttpContext http, CancellationToken ct) =>
                (await query.ClinicAsync(clinicId, page, pageSize, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ClinicAudit").WithSummary("Clinic audit log, newest first (audit.view)")
            .Produces<Page<AuditEntryDto>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        MapBranches(clinic);
        MapCatalog(clinic);
        MapStaff(clinic);
        MapDoctors(clinic);
        return app;
    }

    private static void MapBranches(RouteGroupBuilder clinic)
    {
        clinic.MapGet("/branches", async (Guid clinicId, BranchHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ListAsync(clinicId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ListBranches").WithSummary("Branches with opening hours (Branches-scoped members: own branches)")
            .Produces<IReadOnlyList<BranchDto>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPost("/branches", async (Guid clinicId, BranchCommand body, BranchHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CreateAsync(clinicId, body, ct)).ToHttp(http, r => TypedResults.Created($"/api/clinics/{clinicId}/branches/{r.Id}", r)))
            .WithName("CreateBranch").WithSummary("Create a branch (branch.manage, clinic-wide)")
            .Produces<BranchDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPut("/branches/{branchId:guid}", async (Guid clinicId, Guid branchId, BranchCommand body, BranchHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.UpdateAsync(clinicId, branchId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("UpdateBranch").WithSummary("Edit a branch (branch.manage on that branch)")
            .Produces<BranchDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPut("/branches/{branchId:guid}/hours", async (Guid clinicId, Guid branchId, List<OpeningHoursDto> body, BranchHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.SetHoursAsync(clinicId, branchId, body, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("SetBranchHours").WithSummary("Replace the weekly opening hours (branch.manage)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
    }

    private static void MapCatalog(RouteGroupBuilder clinic)
    {
        clinic.MapGet("/services", async (Guid clinicId, CatalogHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ListAsync(clinicId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ListServices").WithSummary("Clinic services with per-branch offers")
            .Produces<IReadOnlyList<ServiceDto>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPost("/services", async (Guid clinicId, ServiceCommand body, CatalogHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CreateAsync(clinicId, body, ct)).ToHttp(http, r => TypedResults.Created($"/api/clinics/{clinicId}/services/{r.Id}", r)))
            .WithName("CreateService").WithSummary("Create a clinic service (catalog.manage, clinic-wide)")
            .Produces<ServiceDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPut("/services/{serviceId:guid}", async (Guid clinicId, Guid serviceId, ServiceCommand body, CatalogHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.UpdateAsync(clinicId, serviceId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("UpdateService").WithSummary("Edit a clinic service (catalog.manage, clinic-wide)")
            .Produces<ServiceDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPut("/branches/{branchId:guid}/services/{serviceId:guid}", async (Guid clinicId, Guid branchId, Guid serviceId, BranchOfferCommand body, CatalogHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.SetOfferAsync(clinicId, branchId, serviceId, body, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("SetBranchOffer").WithSummary("Create or update a branch offer: price, price kind, duration (catalog.manage on the branch)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
    }

    private static void MapStaff(RouteGroupBuilder clinic)
    {
        clinic.MapGet("/staff", async (Guid clinicId, StaffHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ListAsync(clinicId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ListStaff").WithSummary("Memberships the caller may manage (staff.manage)")
            .Produces<IReadOnlyList<StaffMemberDto>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPut("/staff/{membershipId:guid}", async (Guid clinicId, Guid membershipId, StaffChangeCommand body, StaffHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.UpdateAsync(clinicId, membershipId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("UpdateStaff").WithSummary("Change role/scope/branches (no escalation, last-owner guard)")
            .Produces<StaffMemberDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(422);

        clinic.MapDelete("/staff/{membershipId:guid}", async (Guid clinicId, Guid membershipId, StaffHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.RemoveAsync(clinicId, membershipId, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("RemoveStaff").WithSummary("Remove a membership (effective immediately; last-owner guard)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(422);

        clinic.MapGet("/invitations", async (Guid clinicId, ClinicInvitationHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ListAsync(clinicId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ListInvitations").WithSummary("Open staff invitations (staff.manage)")
            .Produces<IReadOnlyList<InvitationDto>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPost("/invitations", async (Guid clinicId, InvitationCommand body, ClinicInvitationHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CreateAsync(clinicId, body, ct)).ToHttp(http, r => TypedResults.Created($"/api/clinics/{clinicId}/invitations/{r.Id}", r)))
            .WithName("CreateInvitation").WithSummary("Invite staff by phone (7-day expiry; replaces an open invitation for the same phone)")
            .Produces<InvitationDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        clinic.MapDelete("/invitations/{invitationId:guid}", async (Guid clinicId, Guid invitationId, ClinicInvitationHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.RevokeAsync(clinicId, invitationId, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("RevokeInvitation").WithSummary("Revoke an open invitation (staff.manage)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
    }

    private static void MapDoctors(RouteGroupBuilder clinic)
    {
        clinic.MapGet("/doctors", async (Guid clinicId, DoctorHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ListAsync(clinicId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ListDoctors").WithSummary("Doctors with branch/service links")
            .Produces<IReadOnlyList<DoctorDto>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPost("/doctors", async (Guid clinicId, DoctorCommand body, DoctorHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.CreateAsync(clinicId, body, ct)).ToHttp(http, r => TypedResults.Created($"/api/clinics/{clinicId}/doctors/{r.Id}", r)))
            .WithName("CreateDoctor").WithSummary("Create a doctor (schedule.manage; userId also needs staff.manage)")
            .Produces<DoctorDto>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        clinic.MapPut("/doctors/{doctorId:guid}", async (Guid clinicId, Guid doctorId, DoctorCommand body, DoctorHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.UpdateAsync(clinicId, doctorId, body, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("UpdateDoctor").WithSummary("Edit a doctor and replace branch/service links")
            .Produces<DoctorDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        clinic.MapGet("/doctors/{doctorId:guid}/working-hours", async (Guid clinicId, Guid doctorId, WorkingHoursHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.GetAsync(clinicId, doctorId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("GetWorkingHours").WithSummary("Weekly working-hour template (branch-local times)")
            .Produces<IReadOnlyList<WorkingHoursEntryDto>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        clinic.MapPut("/doctors/{doctorId:guid}/working-hours", async (Guid clinicId, Guid doctorId, List<WorkingHoursEntryDto> body, WorkingHoursHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.SetAsync(clinicId, doctorId, body, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("SetWorkingHours").WithSummary("Replace the weekly template on the caller's branches (schedule.manage)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(422);
    }
}
