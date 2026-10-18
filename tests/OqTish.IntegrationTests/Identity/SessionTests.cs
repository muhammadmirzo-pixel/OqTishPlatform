using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Identity;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Identity;

public class SessionTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<ApiFactory> FactoryAsync(PostgresFixture postgres) =>
        ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Auth:Otp:ResendCooldownSeconds"] = "0" });

    [Fact]
    public async Task Logout_revokes_only_the_current_session()
    {
        await using var factory = await FactoryAsync(postgres);
        var a = await SignInAsPatientAsync(factory);
        var b = await SignInAsPatientAsync(factory, a.Phone);

        var logout = await a.Client.PostAsync("/api/auth/logout", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-oqtish=;", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Unauthorized, (await a.Client.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Client.GetAsync("/api/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Logout_all_revokes_every_session()
    {
        await using var factory = await FactoryAsync(postgres);
        var a = await SignInAsPatientAsync(factory);
        var b = await SignInAsPatientAsync(factory, a.Phone);

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.PostAsync("/api/auth/logout-all", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.Client.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Client.GetAsync("/api/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Sessions_are_listed_and_revocable_but_not_across_users()
    {
        await using var factory = await FactoryAsync(postgres);
        var a = await SignInAsPatientAsync(factory);
        var b = await SignInAsPatientAsync(factory, a.Phone);
        var stranger = await SignInAsPatientAsync(factory);

        var sessions = (await a.Client.GetFromJsonAsync<List<SessionDto>>("/api/me/sessions", Json, Ct))!;
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, s => s.Current);
        var otherSession = sessions.Single(s => !s.Current);

        await AssertProblemAsync(await stranger.Client.DeleteAsync($"/api/me/sessions/{otherSession.Id}", Ct), HttpStatusCode.NotFound, "common.not_found");
        Assert.Equal(HttpStatusCode.OK, (await b.Client.GetAsync("/api/me", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/me/sessions/{otherSession.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Client.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync("/api/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Suspended_user_gets_401_on_the_next_request()
    {
        await using var factory = await FactoryAsync(postgres);
        var a = await SignInAsPatientAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync("/api/me", Ct)).StatusCode);

        await using (var db = factory.CreateDbContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == a.Me.Id, Ct);
            user.Suspend("test", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(Ct);
        }

        await AssertProblemAsync(await a.Client.GetAsync("/api/me", Ct), HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Fact]
    public async Task Suspended_user_cannot_sign_in_again()
    {
        await using var factory = await FactoryAsync(postgres);
        var a = await SignInAsPatientAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE identity.users SET status = 'Suspended' WHERE id = {a.Me.Id}", Ct);
        }

        var client = factory.CreateApiClient();
        var challenge = await RequestOtpAsync(client, a.Phone);
        var verify = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code = LatestCode(factory, a.Phone) }, Ct);
        await AssertProblemAsync(verify, HttpStatusCode.Forbidden, "auth.account_suspended");
    }

    [Fact]
    public async Task Revoked_session_in_db_is_rejected_immediately_and_idle_session_expires()
    {
        await using var factory = await FactoryAsync(postgres);
        var a = await SignInAsPatientAsync(factory);
        var b = await SignInAsPatientAsync(factory, a.Phone);
        await using var db = factory.CreateDbContext();
        var ids = await db.Sessions.Where(s => s.UserId == a.Me.Id).OrderBy(s => s.CreatedAt).Select(s => s.Id).ToListAsync(Ct);

        await db.Database.ExecuteSqlAsync($"UPDATE identity.sessions SET revoked_at = now() WHERE id = {ids[0]}", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.Client.GetAsync("/api/me", Ct)).StatusCode);

        await db.Database.ExecuteSqlAsync($"UPDATE identity.sessions SET last_seen_at = now() - interval '15 days' WHERE id = {ids[1]}", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Client.GetAsync("/api/me", Ct)).StatusCode);
    }
}
