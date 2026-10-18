using System.Net;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Identity;
using OqTish.Application.Staff;
using OqTish.Domain.Staff;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Console;

/// <summary>Staff memberships (escalation, last owner, removal) and phone invitations (SECURITY.md §8, S11).</summary>
public class StaffAndInvitationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Grant(StaffRole role, params Guid[] branches) =>
        new { role = role.ToString(), scope = branches.Length == 0 ? "Clinic" : "Branches", branchIds = branches };

    private static async Task<Guid> MembershipIdAsync(ApiFactory factory, Guid clinicId, Guid userId)
    {
        await using var db = factory.CreateDbContext();
        return await db.ClinicMemberships.Where(m => m.ClinicId == clinicId && m.UserId == userId).Select(m => m.Id).SingleAsync(Ct);
    }

    [Fact]
    public async Task Staff_list_is_scoped_and_needs_staff_manage()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var b = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var branchAdmin = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicAdmin, [a.BranchIds[0]]);
        var r0 = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist, [a.BranchIds[0]]);
        await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist, [a.BranchIds[1]]);
        var otherOwner = await SignInAsStaffAsync(factory, b.ClinicId, StaffRole.ClinicOwner);
        var url = $"/api/clinics/{a.ClinicId}/staff";

        var all = await owner.Client.GetOkAsync<List<StaffMemberDto>>(url);
        Assert.Equal(4, all.Count);
        Assert.Contains(all, s => s.UserId == r0.Me.Id && s.PhoneMasked == PhoneNumber.Mask(r0.Phone));
        var scoped = await branchAdmin.Client.GetOkAsync<List<StaffMemberDto>>(url);
        Assert.Equal(new[] { branchAdmin.Me.Id, r0.Me.Id }.Order(), scoped.Select(s => s.UserId).Order());
        await AssertProblemAsync(await r0.Client.GetAsync(url, Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await otherOwner.Client.GetAsync(url, Ct), HttpStatusCode.NotFound, "common.not_found");
    }

    [Fact]
    public async Task Escalation_attempts_are_refused()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var b = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var admin = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicAdmin);
        var branchAdmin = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicAdmin, [a.BranchIds[0]]);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist, [a.BranchIds[0]]);
        var otherOwner = await SignInAsStaffAsync(factory, b.ClinicId, StaffRole.ClinicOwner);
        var rid = await MembershipIdAsync(factory, a.ClinicId, receptionist.Me.Id);
        var ownerId = await MembershipIdAsync(factory, a.ClinicId, owner.Me.Id);
        var adminId = await MembershipIdAsync(factory, a.ClinicId, admin.Me.Id);
        var url = $"/api/clinics/{a.ClinicId}/staff";

        await AssertProblemAsync(await admin.Client.PutAsync($"{url}/{rid}", Grant(StaffRole.ClinicAdmin)), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        await AssertProblemAsync(await admin.Client.PutAsync($"{url}/{rid}", Grant(StaffRole.ClinicOwner)), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        await AssertProblemAsync(await admin.Client.PutAsync($"{url}/{ownerId}", Grant(StaffRole.Receptionist)), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        await AssertProblemAsync(await admin.Client.DeleteAsync($"{url}/{ownerId}", Ct), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        await AssertProblemAsync(await admin.Client.PutAsync($"{url}/{adminId}", Grant(StaffRole.ClinicOwner)), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        await AssertProblemAsync(await branchAdmin.Client.PutAsync($"{url}/{rid}", Grant(StaffRole.Receptionist)), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        await AssertProblemAsync(await branchAdmin.Client.PutAsync($"{url}/{rid}", Grant(StaffRole.Receptionist, a.BranchIds[1])), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        await AssertProblemAsync(await receptionist.Client.PutAsync($"{url}/{rid}", Grant(StaffRole.ClinicOwner)), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await otherOwner.Client.PutAsync($"{url}/{rid}", Grant(StaffRole.Doctor)), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await otherOwner.Client.PutAsync($"/api/clinics/{b.ClinicId}/staff/{rid}", Grant(StaffRole.Doctor)), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await owner.Client.PutAsync($"{url}/{rid}", Grant(StaffRole.Doctor, b.BranchIds[0])), HttpStatusCode.BadRequest, "common.validation");

        var changed = await (await branchAdmin.Client.PutAsync($"{url}/{rid}", Grant(StaffRole.Doctor, a.BranchIds[0]))).ReadAsync<StaffMemberDto>();
        Assert.Equal((StaffRole.Doctor, MembershipScope.Branches), (changed.Role, changed.Scope));
        var promoted = await (await owner.Client.PutAsync($"{url}/{rid}", Grant(StaffRole.ClinicAdmin))).ReadAsync<StaffMemberDto>();
        Assert.Equal((StaffRole.ClinicAdmin, MembershipScope.Clinic), (promoted.Role, promoted.Scope));
        Assert.Equal(2, await ConsoleKit.AuditCountAsync(factory, "staff.membership.changed", a.ClinicId));

        // The receptionist's DB state still reflects only successful changes (escalations wrote nothing).
        var me = await receptionist.Client.GetOkAsync<MeDto>("/api/me");
        Assert.Equal(StaffRole.ClinicAdmin, Assert.Single(me.Memberships).Role);
    }

    [Fact]
    public async Task Last_owner_is_kept_and_removal_takes_effect_immediately()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 1);
        var owner1 = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var owner2 = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist);
        var url = $"/api/clinics/{a.ClinicId}/staff";
        var o1 = await MembershipIdAsync(factory, a.ClinicId, owner1.Me.Id);
        var o2 = await MembershipIdAsync(factory, a.ClinicId, owner2.Me.Id);

        await AssertProblemAsync(await owner1.Client.PutAsync($"{url}/{o1}", Grant(StaffRole.Receptionist)), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        (await owner1.Client.DeleteAsync($"{url}/{o2}", Ct)).AssertStatus(HttpStatusCode.NoContent);
        await AssertProblemAsync(await owner2.Client.GetAsync($"/api/clinics/{a.ClinicId}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await owner1.Client.DeleteAsync($"{url}/{o1}", Ct), HttpStatusCode.UnprocessableEntity, "staff.last_owner");

        var rid = await MembershipIdAsync(factory, a.ClinicId, receptionist.Me.Id);
        (await receptionist.Client.GetAsync($"/api/clinics/{a.ClinicId}", Ct)).AssertStatus(HttpStatusCode.OK);
        (await owner1.Client.DeleteAsync($"{url}/{rid}", Ct)).AssertStatus(HttpStatusCode.NoContent);
        await AssertProblemAsync(await receptionist.Client.GetAsync($"/api/clinics/{a.ClinicId}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await owner1.Client.DeleteAsync($"{url}/{rid}", Ct), HttpStatusCode.NotFound, "common.not_found");
        Assert.Equal(2, await ConsoleKit.AuditCountAsync(factory, "staff.membership.removed", a.ClinicId));
    }

    [Fact]
    public async Task Invitation_by_phone_is_accepted_by_the_verified_phone_owner()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var phone = NewPhone();
        var url = $"/api/clinics/{a.ClinicId}/invitations";

        var first = await (await owner.Client.PostAsync(url, new { phone, role = "Doctor", scope = "Clinic", branchIds = Array.Empty<Guid>() })).ReadAsync<InvitationDto>(HttpStatusCode.Created);
        var invitation = await (await owner.Client.PostAsync(url, new { phone, role = "Receptionist", scope = "Branches", branchIds = new[] { a.BranchIds[1] } })).ReadAsync<InvitationDto>(HttpStatusCode.Created);
        Assert.Equal(PhoneNumber.Mask(phone), invitation.PhoneMasked);
        Assert.Equal(invitation.CreatedAt.AddDays(7), invitation.ExpiresAt);
        Assert.Equal([invitation.Id], (await owner.Client.GetOkAsync<List<InvitationDto>>(url)).Select(i => i.Id));

        var stranger = await SignInAsPatientAsync(factory);
        Assert.Empty(await stranger.Client.GetOkAsync<List<MyInvitationDto>>("/api/me/invitations"));
        await AssertProblemAsync(await stranger.Client.PostAsync($"/api/me/invitations/{invitation.Id}/accept", (object?)null), HttpStatusCode.NotFound, "common.not_found");

        var invitee = await SignInAsPatientAsync(factory, phone);
        var mine = Assert.Single(await invitee.Client.GetOkAsync<List<MyInvitationDto>>("/api/me/invitations"));
        Assert.Equal((invitation.Id, StaffRole.Receptionist, "Filial 2"), (mine.Id, mine.Role, Assert.Single(mine.BranchNames)));
        await AssertProblemAsync(await invitee.Client.PostAsync($"/api/me/invitations/{first.Id}/accept", (object?)null), HttpStatusCode.NotFound, "common.not_found");
        (await invitee.Client.PostAsync($"/api/me/invitations/{invitation.Id}/accept", (object?)null)).AssertStatus(HttpStatusCode.NoContent);

        var me = await invitee.Client.GetOkAsync<MeDto>("/api/me");
        var membership = Assert.Single(me.Memberships);
        Assert.Equal((StaffRole.Receptionist, MembershipScope.Branches), (membership.Role, membership.Scope));
        Assert.Equal([a.BranchIds[1]], membership.BranchIds);
        await AssertProblemAsync(await invitee.Client.PostAsync($"/api/me/invitations/{invitation.Id}/accept", (object?)null), HttpStatusCode.NotFound, "common.not_found");
        Assert.Empty(await owner.Client.GetOkAsync<List<InvitationDto>>(url));
        await AssertProblemAsync(await owner.Client.PostAsync(url, new { phone, role = "Doctor", scope = "Clinic" }), HttpStatusCode.Conflict, "staff.already_member");
        Assert.Equal(1, await ConsoleKit.AuditCountAsync(factory, "staff.invitation.accepted", a.ClinicId));

        // A removed member can be invited again; the membership is reactivated with the new grant.
        var mid = membership.MembershipId;
        (await owner.Client.DeleteAsync($"/api/clinics/{a.ClinicId}/staff/{mid}", Ct)).AssertStatus(HttpStatusCode.NoContent);
        var again = await (await owner.Client.PostAsync(url, new { phone, role = "Doctor", scope = "Clinic" })).ReadAsync<InvitationDto>(HttpStatusCode.Created);
        (await invitee.Client.PostAsync($"/api/me/invitations/{again.Id}/accept", (object?)null)).AssertStatus(HttpStatusCode.NoContent);
        var back = Assert.Single((await invitee.Client.GetOkAsync<MeDto>("/api/me")).Memberships);
        Assert.Equal((mid, StaffRole.Doctor, MembershipScope.Clinic), (back.MembershipId, back.Role, back.Scope));
    }

    [Fact]
    public async Task Expired_or_revoked_invitations_cannot_be_accepted()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var phone = NewPhone();
        var url = $"/api/clinics/{a.ClinicId}/invitations";
        var invitation = await (await owner.Client.PostAsync(url, new { phone, role = "Doctor", scope = "Clinic" })).ReadAsync<InvitationDto>(HttpStatusCode.Created);
        var invitee = await SignInAsPatientAsync(factory, phone);

        await using (var db = factory.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE staff.staff_invitations SET created_at = now() - interval '8 days', expires_at = now() - interval '1 day' WHERE id = {invitation.Id}", Ct);
        }

        Assert.Empty(await invitee.Client.GetOkAsync<List<MyInvitationDto>>("/api/me/invitations"));
        await AssertProblemAsync(await invitee.Client.PostAsync($"/api/me/invitations/{invitation.Id}/accept", (object?)null), HttpStatusCode.UnprocessableEntity, "staff.invitation_expired");
        Assert.Empty((await invitee.Client.GetOkAsync<MeDto>("/api/me")).Memberships);

        var fresh = await (await owner.Client.PostAsync(url, new { phone, role = "Doctor", scope = "Clinic" })).ReadAsync<InvitationDto>(HttpStatusCode.Created);
        (await owner.Client.DeleteAsync($"{url}/{fresh.Id}", Ct)).AssertStatus(HttpStatusCode.NoContent);
        await AssertProblemAsync(await invitee.Client.PostAsync($"/api/me/invitations/{fresh.Id}/accept", (object?)null), HttpStatusCode.NotFound, "common.not_found");
        Assert.Equal(1, await ConsoleKit.AuditCountAsync(factory, "staff.invitation.revoked", a.ClinicId));
    }

    [Fact]
    public async Task Invitations_follow_the_same_escalation_rules()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var b = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var admin = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicAdmin, [a.BranchIds[0]]);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist);
        var otherOwner = await SignInAsStaffAsync(factory, b.ClinicId, StaffRole.ClinicOwner);
        var url = $"/api/clinics/{a.ClinicId}/invitations";

        await AssertProblemAsync(await admin.Client.PostAsync(url, new { phone = NewPhone(), role = "ClinicAdmin", scope = "Branches", branchIds = new[] { a.BranchIds[0] } }), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        await AssertProblemAsync(await admin.Client.PostAsync(url, new { phone = NewPhone(), role = "Doctor", scope = "Clinic" }), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        await AssertProblemAsync(await admin.Client.PostAsync(url, new { phone = NewPhone(), role = "Doctor", scope = "Branches", branchIds = new[] { a.BranchIds[1] } }), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        (await admin.Client.PostAsync(url, new { phone = NewPhone(), role = "Doctor", scope = "Branches", branchIds = new[] { a.BranchIds[0] } })).AssertStatus(HttpStatusCode.Created);
        var ownerInvite = await (await owner.Client.PostAsync(url, new { phone = NewPhone(), role = "ClinicAdmin", scope = "Clinic" })).ReadAsync<InvitationDto>(HttpStatusCode.Created);
        Assert.Single(await admin.Client.GetOkAsync<List<InvitationDto>>(url));
        await AssertProblemAsync(await admin.Client.DeleteAsync($"{url}/{ownerInvite.Id}", Ct), HttpStatusCode.Forbidden, "staff.cannot_grant_role");
        await AssertProblemAsync(await receptionist.Client.PostAsync(url, new { phone = NewPhone(), role = "Doctor", scope = "Clinic" }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await receptionist.Client.GetAsync(url, Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await otherOwner.Client.PostAsync(url, new { phone = NewPhone(), role = "Doctor", scope = "Clinic" }), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await otherOwner.Client.DeleteAsync($"/api/clinics/{b.ClinicId}/invitations/{ownerInvite.Id}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await owner.Client.PostAsync(url, new { phone = "12345", role = "Doctor", scope = "Clinic" }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await owner.Client.PostAsync(url, new { phone = NewPhone(), role = "Doctor", scope = "Branches", branchIds = Array.Empty<Guid>() }), HttpStatusCode.BadRequest, "common.validation");

        await using var db = factory.CreateDbContext();
        Assert.DoesNotContain(await db.AuditLog.Where(x => x.Action == "staff.invitation.created").Select(x => x.Details!).ToListAsync(Ct), d => d.Contains("+998", StringComparison.Ordinal));
        Assert.All(await db.StaffInvitations.ToListAsync(Ct), i => Assert.Equal(64, i.PhoneHash.Length));
    }
}
