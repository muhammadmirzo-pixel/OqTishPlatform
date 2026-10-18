using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Identity;
using OqTish.Domain.Identity;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Identity;

public class OtpSignInTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Happy_path_creates_patient_and_session_cookie()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var phone = NewPhone();
        var signedIn = await SignInAsPatientAsync(factory, phone);

        Assert.True(signedIn.Me.PhoneVerified);
        Assert.Equal(PhoneNumber.Mask(phone), signedIn.Me.PhoneMasked);
        Assert.NotNull(signedIn.Me.Patient);

        var me = await signedIn.Client.GetAsync("/api/me", Ct);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.AuthIdentities.CountAsync(i => i.NormalizedIdentifier == phone, Ct));
        Assert.Equal(1, await db.Sessions.CountAsync(s => s.UserId == signedIn.Me.Id, Ct));
        Assert.True(await db.AuditLog.AnyAsync(a => a.Action == "auth.signed_in" && a.ActorUserId == signedIn.Me.Id, Ct));
    }

    [Fact]
    public async Task Session_cookie_is_host_prefixed_httponly_secure_lax()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        var phone = NewPhone();
        var challenge = await RequestOtpAsync(client, phone);
        var verify = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code = LatestCode(factory, phone) }, Ct);

        var setCookie = Assert.Single(verify.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-oqtish=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Second_sign_in_with_same_phone_reuses_the_account()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Auth:Otp:ResendCooldownSeconds"] = "0" });
        var phone = NewPhone();
        var first = await SignInAsPatientAsync(factory, phone);
        var second = await SignInAsPatientAsync(factory, phone);
        Assert.Equal(first.Me.Id, second.Me.Id);
    }

    [Fact]
    public async Task Wrong_code_is_rejected()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        var phone = NewPhone();
        var challenge = await RequestOtpAsync(client, phone);
        var wrong = WrongCode(LatestCode(factory, phone));

        var response = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code = wrong }, Ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "auth.otp_invalid");
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        var phone = NewPhone();
        var challenge = await RequestOtpAsync(client, phone);
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE identity.otp_challenges SET created_at = now() - interval '10 minutes', expires_at = now() - interval '1 second' WHERE id = {challenge.ChallengeId}", Ct);
        }

        var response = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code = LatestCode(factory, phone) }, Ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "auth.otp_expired");
    }

    [Fact]
    public async Task Sixth_attempt_fails_even_with_the_correct_code()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        var phone = NewPhone();
        var challenge = await RequestOtpAsync(client, phone);
        var code = LatestCode(factory, phone);

        for (var i = 1; i <= 5; i++)
        {
            var r = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code = WrongCode(code) }, Ct);
            await AssertProblemAsync(r, HttpStatusCode.BadRequest, i < 5 ? "auth.otp_invalid" : "auth.otp_attempts_exceeded");
        }

        var sixth = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code }, Ct);
        await AssertProblemAsync(sixth, HttpStatusCode.BadRequest, "auth.otp_attempts_exceeded");
    }

    [Fact]
    public async Task Code_cannot_be_used_twice()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        var phone = NewPhone();
        var challenge = await RequestOtpAsync(client, phone);
        var code = LatestCode(factory, phone);
        var first = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code }, Ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code }, Ct);
        await AssertProblemAsync(second, HttpStatusCode.BadRequest, "auth.otp_invalid");
    }

    [Fact]
    public async Task Resend_within_cooldown_is_rate_limited_with_retry_after()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        var phone = NewPhone();
        await RequestOtpAsync(client, phone);

        var again = await client.PostAsJsonAsync("/api/auth/otp/request", new { phone }, Ct);

        await AssertProblemAsync(again, HttpStatusCode.TooManyRequests, "auth.rate_limited");
        Assert.True(again.Headers.RetryAfter is not null);
        Assert.Single(factory.Sms.For(phone));
    }

    [Fact]
    public async Task Per_phone_limit_is_three_per_window()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Auth:Otp:ResendCooldownSeconds"] = "0" });
        var client = factory.CreateApiClient();
        var phone = NewPhone();
        for (var i = 0; i < 3; i++)
        {
            await RequestOtpAsync(client, phone);
        }

        var fourth = await client.PostAsJsonAsync("/api/auth/otp/request", new { phone = phone.Replace("+998", "998 ", StringComparison.Ordinal) }, Ct);
        await AssertProblemAsync(fourth, HttpStatusCode.TooManyRequests, "auth.rate_limited");
        Assert.Equal(3, factory.Sms.For(phone).Count);
    }

    [Fact]
    public async Task Per_ip_limit_applies_across_phones()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Auth:Otp:MaxPerIp"] = "2" });
        var client = factory.CreateApiClient();
        await RequestOtpAsync(client, NewPhone());
        await RequestOtpAsync(client, NewPhone());
        var third = await client.PostAsJsonAsync("/api/auth/otp/request", new { phone = NewPhone() }, Ct);
        await AssertProblemAsync(third, HttpStatusCode.TooManyRequests, "auth.rate_limited");
    }

    [Fact]
    public async Task Aspnet_rate_limiter_rejects_bursts_per_ip()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["RateLimiting:OtpSendPermitPerMinute"] = "2" });
        var client = factory.CreateApiClient();
        await RequestOtpAsync(client, NewPhone());
        await RequestOtpAsync(client, NewPhone());
        var third = await client.PostAsJsonAsync("/api/auth/otp/request", new { phone = NewPhone() }, Ct);
        await AssertProblemAsync(third, HttpStatusCode.TooManyRequests, "auth.rate_limited");
    }

    [Fact]
    public async Task Known_and_unknown_numbers_get_identical_responses()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var known = await SignInAsPatientAsync(factory);
        var client = factory.CreateApiClient();

        await using (var db = factory.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"DELETE FROM identity.otp_challenges", Ct);
        }

        var a = await client.PostAsJsonAsync("/api/auth/otp/request", new { phone = known.Phone }, Ct);
        var b = await client.PostAsJsonAsync("/api/auth/otp/request", new { phone = NewPhone() }, Ct);

        Assert.Equal(a.StatusCode, b.StatusCode);
        var ja = await ProblemAsync(a);
        var jb = await ProblemAsync(b);
        Assert.Equal(
            ja.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal),
            jb.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(ja.GetProperty("expiresInSeconds").GetInt32(), jb.GetProperty("expiresInSeconds").GetInt32());
        Assert.Equal(ja.GetProperty("resendAfterSeconds").GetInt32(), jb.GetProperty("resendAfterSeconds").GetInt32());
    }

    [Fact]
    public async Task Sms_failure_does_not_change_the_response()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        factory.Sms.FailSends = true;
        var response = await client.PostAsJsonAsync("/api/auth/otp/request", new { phone = NewPhone() }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("+7 900 123 45 67")]
    [InlineData("not a phone")]
    public async Task Invalid_phone_is_a_validation_error(string phone)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/otp/request", new { phone }, Ct);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "common.validation");
        var body = await ProblemAsync(response);
        Assert.True(body.GetProperty("errors").TryGetProperty("phone", out _));
    }

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";
}
