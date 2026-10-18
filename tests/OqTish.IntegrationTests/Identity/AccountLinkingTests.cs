using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OqTish.Application.Identity;
using OqTish.Application.Identity.Handlers;
using OqTish.Domain.Identity;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Identity;

public class AccountLinkingTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SignedInClient> TelegramUserAsync(ApiFactory factory, long telegramId)
    {
        var client = factory.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/auth/telegram/webapp", new { initData = TelegramSigning.InitData(telegramId, DateTimeOffset.UtcNow) }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new SignedInClient(client, (await response.Content.ReadFromJsonAsync<MeDto>(Json, Ct))!, string.Empty);
    }

    [Fact]
    public async Task Linking_a_phone_owned_by_another_account_conflicts_without_merge()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Auth:Otp:ResendCooldownSeconds"] = "0" });
        var owner = await SignInAsPatientAsync(factory);
        var other = await TelegramUserAsync(factory, 7001);

        var request = await other.Client.PostAsJsonAsync("/api/me/phone/request", new { phone = owner.Phone }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var challenge = (await request.Content.ReadFromJsonAsync<OtpRequestedDto>(Json, Ct))!;
        var verify = await other.Client.PostAsJsonAsync("/api/me/phone/verify", new { challengeId = challenge.ChallengeId, code = LatestCode(factory, owner.Phone) }, Ct);

        await AssertProblemAsync(verify, HttpStatusCode.Conflict, "auth.identity_in_use");
        await using var db = factory.CreateDbContext();
        var phoneIdentities = await db.AuthIdentities.Where(i => i.NormalizedIdentifier == owner.Phone).ToListAsync(Ct);
        Assert.Equal(owner.Me.Id, Assert.Single(phoneIdentities).UserId);
        Assert.False(await db.AuthIdentities.AnyAsync(i => i.UserId == other.Me.Id && i.Kind == AuthIdentityKind.Phone, Ct));
        Assert.Equal(2, await db.Users.CountAsync(u => u.Id == owner.Me.Id || u.Id == other.Me.Id, Ct));
        Assert.True(await db.AuditLog.AnyAsync(a => a.Action == "identity.link_conflict" && a.ActorUserId == other.Me.Id, Ct));
    }

    [Fact]
    public async Task Linking_a_free_phone_succeeds_and_challenge_is_bound_to_the_account()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var tg = await TelegramUserAsync(factory, 7002);
        var intruder = await TelegramUserAsync(factory, 7003);
        var phone = NewPhone();

        var request = await tg.Client.PostAsJsonAsync("/api/me/phone/request", new { phone }, Ct);
        var challenge = (await request.Content.ReadFromJsonAsync<OtpRequestedDto>(Json, Ct))!;
        var code = LatestCode(factory, phone);

        var stolen = await intruder.Client.PostAsJsonAsync("/api/me/phone/verify", new { challengeId = challenge.ChallengeId, code }, Ct);
        await AssertProblemAsync(stolen, HttpStatusCode.BadRequest, "auth.otp_invalid");

        var verify = await tg.Client.PostAsJsonAsync("/api/me/phone/verify", new { challengeId = challenge.ChallengeId, code }, Ct);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var me = (await verify.Content.ReadFromJsonAsync<MeDto>(Json, Ct))!;
        Assert.True(me.PhoneVerified && me.TelegramLinked);
    }

    [Fact]
    public async Task Telegram_link_token_links_once_and_conflicts_never_merge()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var patient = await SignInAsPatientAsync(factory);
        var linkResponse = await patient.Client.PostAsync("/api/me/telegram/link", null, Ct);
        Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);
        var link = (await linkResponse.Content.ReadFromJsonAsync<TelegramLinkDto>(Json, Ct))!;
        Assert.StartsWith("https://t.me/oqtish_test_bot?start=", link.DeepLink, StringComparison.Ordinal);
        var token = link.DeepLink.Split("start=")[1];

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<TelegramLinkService>();
            var linked = await service.ConsumeTelegramLinkTokenAsync(token, 8001, 8001, Ct);
            Assert.True(linked.IsSuccess);
            Assert.Equal(patient.Me.Id, linked.Value.UserId);
            var again = await service.ConsumeTelegramLinkTokenAsync(token, 8001, 8001, Ct);
            Assert.Equal("telegram.link_invalid", again.Error!.Code);
        }

        await using (var db = factory.CreateDbContext())
        {
            Assert.False(await db.TelegramLinkTokens.AnyAsync(t => t.TokenHash == token, Ct));
            Assert.Equal(8001, (await db.TelegramChatLinks.SingleAsync(c => c.UserId == patient.Me.Id, Ct)).ChatId);
        }

        // A second account trying to link the same Telegram id → identity_in_use, no merge.
        var second = await SignInAsPatientAsync(factory);
        var secondLink = (await (await second.Client.PostAsync("/api/me/telegram/link", null, Ct)).Content.ReadFromJsonAsync<TelegramLinkDto>(Json, Ct))!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<TelegramLinkService>()
                .ConsumeTelegramLinkTokenAsync(secondLink.DeepLink.Split("start=")[1], 8001, 8001, Ct);
            Assert.Equal("auth.identity_in_use", result.Error!.Code);
        }

        // Telegram sign-in with that id lands in the first account.
        var tgClient = factory.CreateApiClient();
        var signIn = await tgClient.PostAsJsonAsync("/api/auth/telegram/webapp", new { initData = TelegramSigning.InitData(8001, DateTimeOffset.UtcNow) }, Ct);
        Assert.Equal(patient.Me.Id, (await signIn.Content.ReadFromJsonAsync<MeDto>(Json, Ct))!.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await patient.Client.DeleteAsync("/api/me/telegram", Ct)).StatusCode);
        var me = await patient.Client.GetFromJsonAsync<MeDto>("/api/me", Json, Ct);
        Assert.False(me!.TelegramLinked);
    }

    [Fact]
    public async Task Telegram_only_account_cannot_unlink_its_last_identity()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var tg = await TelegramUserAsync(factory, 7004);
        await AssertProblemAsync(await tg.Client.DeleteAsync("/api/me/telegram", Ct), HttpStatusCode.UnprocessableEntity, "auth.last_identity");
    }
}
