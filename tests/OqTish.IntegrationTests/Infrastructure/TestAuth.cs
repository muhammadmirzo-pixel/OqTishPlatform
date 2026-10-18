using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using OqTish.Application.Common.Json;
using OqTish.Application.Identity;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.IntegrationTests.Infrastructure;

/// <summary>A signed-in test client.</summary>
public sealed record SignedInClient(HttpClient Client, MeDto Me, string Phone);

/// <summary>A clinic with branches created directly in the database for tests.</summary>
public sealed record TestClinic(Guid ClinicId, IReadOnlyList<Guid> BranchIds);

/// <summary>
/// Real sign-in flows for tests (no auth bypass): OTP through the Fake SMS mailbox. Staff = a signed-in
/// user plus a membership inserted directly in the database.
/// </summary>
public static partial class TestAuth
{
    private static int _phoneCounter = Random.Shared.Next(1_000_000, 9_000_000);

    public static JsonSerializerOptions Json => JsonDefaults.Options;

    /// <summary>A fresh, unique valid number (+99899xxxxxxx).</summary>
    public static string NewPhone() => "+99899" + Interlocked.Increment(ref _phoneCounter).ToString("D7", System.Globalization.CultureInfo.InvariantCulture);

    public static async Task<OtpRequestedDto> RequestOtpAsync(HttpClient client, string phone)
    {
        var response = await client.PostAsJsonAsync("/api/auth/otp/request", new { phone }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OtpRequestedDto>(Json, TestContext.Current.CancellationToken))!;
    }

    /// <summary>The latest 6-digit code sent to the phone.</summary>
    public static string LatestCode(ApiFactory factory, string phone)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var messages = factory.Sms.For(phone);
        var message = messages.Count > 0 ? messages[0] : throw new InvalidOperationException("No SMS captured.");
        return CodePattern().Match(message.Text).Value;
    }

    public static async Task<SignedInClient> SignInAsPatientAsync(ApiFactory factory, string? phone = null, HttpClient? client = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        phone ??= NewPhone();
        client ??= factory.CreateApiClient();
        var challenge = await RequestOtpAsync(client, phone);
        var verify = await client.PostAsJsonAsync("/api/auth/otp/verify",
            new { challengeId = challenge.ChallengeId, code = LatestCode(factory, phone) }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var me = (await verify.Content.ReadFromJsonAsync<MeDto>(Json, TestContext.Current.CancellationToken))!;
        return new SignedInClient(client, me, phone);
    }

    public static async Task<TestClinic> CreateClinicAsync(ApiFactory factory, int branches = 2, ClinicVerificationStatus status = ClinicVerificationStatus.Approved)
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var db = factory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var clinic = Clinic.Register("Test klinika " + Guid.NewGuid().ToString("N")[..6], null, null, null, null, now);
        switch (status)
        {
            case ClinicVerificationStatus.Approved: clinic.Approve(now); break;
            case ClinicVerificationStatus.Suspended: clinic.Suspend("test", now); break;
            case ClinicVerificationStatus.Rejected: clinic.Reject("test", now); break;
        }

        db.Clinics.Add(clinic);
        var ids = new List<Guid>();
        for (var i = 0; i < branches; i++)
        {
            var branch = Branch.Create(clinic.Id, $"Filial {i + 1}", "Yunusobod", "Test ko'chasi " + (i + 1), null, null, null, null, now);
            db.Branches.Add(branch);
            ids.Add(branch.Id);
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new TestClinic(clinic.Id, ids);
    }

    public static async Task<Guid> AddMembershipAsync(ApiFactory factory, Guid userId, Guid clinicId, StaffRole role, IReadOnlyList<Guid>? branchIds = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var db = factory.CreateDbContext();
        var membership = ClinicMembership.Create(clinicId, userId, role, branchIds is null ? MembershipScope.Clinic : MembershipScope.Branches, DateTimeOffset.UtcNow);
        db.ClinicMemberships.Add(membership);
        foreach (var branchId in branchIds ?? [])
        {
            db.BranchAccess.Add(BranchAccess.Create(clinicId, membership.Id, branchId));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return membership.Id;
    }

    /// <summary>Signs in a new user and gives it a membership (Clinic scope, or Branches scope when <paramref name="branchIds"/> is set).</summary>
    public static async Task<SignedInClient> SignInAsStaffAsync(ApiFactory factory, Guid clinicId, StaffRole role, IReadOnlyList<Guid>? branchIds = null)
    {
        var signedIn = await SignInAsPatientAsync(factory);
        await AddMembershipAsync(factory, signedIn.Me.Id, clinicId, role, branchIds);
        return signedIn;
    }

    /// <summary>Runs TOTP setup + enable through the API; returns the base32 secret.</summary>
    public static async Task<string> EnableTotpAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        var ct = TestContext.Current.CancellationToken;
        var setup = await client.PostAsync("/api/me/totp/setup", null, ct);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var dto = (await setup.Content.ReadFromJsonAsync<TotpSetupDto>(Json, ct))!;
        var enable = await client.PostAsJsonAsync("/api/me/totp/enable", new { code = TotpCode(dto.Secret, 0) }, ct);
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);
        return dto.Secret;
    }

    /// <summary>The code for the current step + <paramref name="stepOffset"/> (±1 is accepted by the server).</summary>
    public static string TotpCode(string base32Secret, int stepOffset) =>
        Totp.Compute(Base32.Decode(base32Secret)!, Totp.StepAt(DateTimeOffset.UtcNow) + stepOffset);

    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    public static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == status, $"Expected {(int)status} {code}, got {(int)response.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("traceId").GetString()));
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();
}
