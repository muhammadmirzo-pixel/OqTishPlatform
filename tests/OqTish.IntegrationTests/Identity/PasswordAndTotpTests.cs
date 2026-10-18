using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Identity;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Identity;

public class PasswordAndTotpTests(PostgresFixture postgres)
{
    private const string Password = "Sh0rt-but-ok!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(SignedInClient User, string Email)> StaffWithPasswordAsync(ApiFactory factory)
    {
        var user = await SignInAsPatientAsync(factory);
        var email = $"staff{Guid.NewGuid():N}@example.test";
        var set = await user.Client.PostAsJsonAsync("/api/me/password", new { email, newPassword = Password }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        return (user, email);
    }

    [Fact]
    public async Task Password_login_succeeds_and_wrong_or_unknown_credentials_are_identical()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var (user, email) = await StaffWithPasswordAsync(factory);
        var client = factory.CreateApiClient();

        var ok = await client.PostAsJsonAsync("/api/auth/password/login", new { email = email.ToUpperInvariant(), password = Password }, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(user.Me.Id, (await ok.Content.ReadFromJsonAsync<MeDto>(Json, Ct))!.Id);

        var wrong = await client.PostAsJsonAsync("/api/auth/password/login", new { email, password = "wrong-password-1" }, Ct);
        var unknown = await client.PostAsJsonAsync("/api/auth/password/login", new { email = "nobody@example.test", password = Password }, Ct);
        await AssertProblemAsync(wrong, HttpStatusCode.Unauthorized, "auth.invalid_credentials");
        await AssertProblemAsync(unknown, HttpStatusCode.Unauthorized, "auth.invalid_credentials");
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Auth:Password:MaxFailedAttempts"] = "3" });
        var (_, email) = await StaffWithPasswordAsync(factory);
        var client = factory.CreateApiClient();
        for (var i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync("/api/auth/password/login", new { email, password = "wrong-password-1" }, Ct);
        }

        var locked = await client.PostAsJsonAsync("/api/auth/password/login", new { email, password = Password }, Ct);
        await AssertProblemAsync(locked, HttpStatusCode.Unauthorized, "auth.invalid_credentials");
    }

    [Fact]
    public async Task Totp_required_invalid_valid_and_replay()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var (user, email) = await StaffWithPasswordAsync(factory);
        var secret = await EnableTotpAsync(user.Client);
        var client = factory.CreateApiClient();

        var missing = await client.PostAsJsonAsync("/api/auth/password/login", new { email, password = Password }, Ct);
        await AssertProblemAsync(missing, HttpStatusCode.Unauthorized, "auth.totp_required");

        var invalid = await client.PostAsJsonAsync("/api/auth/password/login", new { email, password = Password, totpCode = "000000" == TotpCode(secret, 1) ? "111111" : "000000" }, Ct);
        await AssertProblemAsync(invalid, HttpStatusCode.Unauthorized, "auth.totp_invalid");

        // The code used to enable TOTP (current step) can never be used again.
        var replayOfEnable = await client.PostAsJsonAsync("/api/auth/password/login", new { email, password = Password, totpCode = TotpCode(secret, 0) }, Ct);
        await AssertProblemAsync(replayOfEnable, HttpStatusCode.Unauthorized, "auth.totp_invalid");

        var code = TotpCode(secret, 1);
        var valid = await client.PostAsJsonAsync("/api/auth/password/login", new { email, password = Password, totpCode = code }, Ct);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.True((await valid.Content.ReadFromJsonAsync<MeDto>(Json, Ct))!.TotpEnabled);

        var replay = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/password/login", new { email, password = Password, totpCode = code }, Ct);
        await AssertProblemAsync(replay, HttpStatusCode.Unauthorized, "auth.totp_invalid");
    }

    [Fact]
    public async Task Wrong_totp_codes_lock_the_second_factor()
    {
        // Review R2: TOTP guesses are counted per user (not per IP), so even the correct code is refused
        // once the limit is reached — on every sign-in path that calls TotpService.
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Auth:Totp:MaxFailedAttempts"] = "3" });
        var (user, email) = await StaffWithPasswordAsync(factory);
        var secret = await EnableTotpAsync(user.Client);
        var wrong = TotpCode(secret, 1) == "000000" ? "111111" : "000000";

        for (var i = 0; i < 3; i++)
        {
            var attempt = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/password/login", new { email, password = Password, totpCode = wrong }, Ct);
            await AssertProblemAsync(attempt, HttpStatusCode.Unauthorized, "auth.totp_invalid");
        }

        var correctButLocked = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/password/login", new { email, password = Password, totpCode = TotpCode(secret, 1) }, Ct);
        await AssertProblemAsync(correctButLocked, HttpStatusCode.TooManyRequests, "auth.totp_locked");
    }

    [Fact]
    public async Task Otp_sign_in_of_totp_account_requires_totp()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Auth:Otp:ResendCooldownSeconds"] = "0" });
        var user = await SignInAsPatientAsync(factory);
        var secret = await EnableTotpAsync(user.Client);
        var client = factory.CreateApiClient();

        var challenge = await RequestOtpAsync(client, user.Phone);
        var code = LatestCode(factory, user.Phone);
        var noTotp = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code }, Ct);
        await AssertProblemAsync(noTotp, HttpStatusCode.Unauthorized, "auth.totp_required");

        var withTotp = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code, totpCode = TotpCode(secret, 1) }, Ct);
        Assert.Equal(HttpStatusCode.OK, withTotp.StatusCode);
    }

    [Fact]
    public async Task Enabling_totp_revokes_other_sessions_and_setup_twice_conflicts()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Auth:Otp:ResendCooldownSeconds"] = "0" });
        var user = await SignInAsPatientAsync(factory);
        var other = await SignInAsPatientAsync(factory, user.Phone);
        var secret = await EnableTotpAsync(user.Client);

        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.Client.GetAsync("/api/me", Ct)).StatusCode);
        await AssertProblemAsync(await user.Client.PostAsync("/api/me/totp/setup", null, Ct), HttpStatusCode.Conflict, "auth.totp_already_enabled");

        var wrongDisable = await user.Client.PostAsJsonAsync("/api/me/totp/disable", new { code = "12345x" }, Ct);
        await AssertProblemAsync(wrongDisable, HttpStatusCode.BadRequest, "auth.totp_invalid");
        var disable = await user.Client.PostAsJsonAsync("/api/me/totp/disable", new { code = TotpCode(secret, 1) }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
    }

    [Fact]
    public async Task Totp_secret_is_encrypted_at_rest()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var user = await SignInAsPatientAsync(factory);
        var secret = await EnableTotpAsync(user.Client);
        await using var db = factory.CreateDbContext();
        var stored = await db.TotpCredentials.SingleAsync(t => t.UserId == user.Me.Id, Ct);
        Assert.DoesNotContain(secret, stored.ProtectedSecret, StringComparison.Ordinal);
        Assert.True(await db.DataProtectionKeys.AnyAsync(Ct));
    }

    [Fact]
    public async Task Changing_password_needs_current_password_and_revokes_other_sessions()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var (user, email) = await StaffWithPasswordAsync(factory);
        var second = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsJsonAsync("/api/auth/password/login", new { email, password = Password }, Ct)).StatusCode);

        var noCurrent = await user.Client.PostAsJsonAsync("/api/me/password", new { newPassword = "Another-pass-2" }, Ct);
        await AssertProblemAsync(noCurrent, HttpStatusCode.BadRequest, "auth.current_password_invalid");

        var changed = await user.Client.PostAsJsonAsync("/api/me/password", new { currentPassword = Password, newPassword = "Another-pass-2" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Email_already_used_by_another_account_conflicts()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var (_, email) = await StaffWithPasswordAsync(factory);
        var other = await SignInAsPatientAsync(factory);
        var response = await other.Client.PostAsJsonAsync("/api/me/password", new { email, newPassword = Password }, Ct);
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "auth.identity_in_use");
    }

    [Fact]
    public async Task Password_reset_flow_and_generic_responses()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var (user, email) = await StaffWithPasswordAsync(factory);
        var other = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync("/api/auth/password/login", new { email, password = Password }, Ct)).StatusCode);
        var anon = factory.CreateApiClient();

        var known = await anon.PostAsJsonAsync("/api/auth/password/reset/request", new { email }, Ct);
        var unknown = await anon.PostAsJsonAsync("/api/auth/password/reset/request", new { email = "ghost@example.test" }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        var knownBody = await known.Content.ReadFromJsonAsync<PasswordResetRequestedDto>(Json, Ct);
        var unknownBody = await unknown.Content.ReadFromJsonAsync<PasswordResetRequestedDto>(Json, Ct);
        Assert.NotEqual(Guid.Empty, unknownBody!.ChallengeId);

        var decoy = await anon.PostAsJsonAsync("/api/auth/password/reset/confirm", new { challengeId = unknownBody.ChallengeId, code = "123456", newPassword = "Brand-new-pass-3" }, Ct);
        await AssertProblemAsync(decoy, HttpStatusCode.BadRequest, "auth.otp_invalid");

        var code = LatestCode(factory, user.Phone);
        var confirm = await anon.PostAsJsonAsync("/api/auth/password/reset/confirm", new { challengeId = knownBody!.ChallengeId, code, newPassword = "Brand-new-pass-3" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.Client.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/me", Ct)).StatusCode);
        var login = await anon.PostAsJsonAsync("/api/auth/password/login", new { email, password = "Brand-new-pass-3" }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
