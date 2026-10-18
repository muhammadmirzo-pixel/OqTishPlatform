using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OqTish.Api.Common;
using OqTish.Application.Common;
using OqTish.Application.Common.Seeding;
using OqTish.Application.Identity;
using OqTish.Application.Notifications;
using OqTish.Application.Notifications.Telegram;
using OqTish.Infrastructure.Persistence;
using OqTish.Infrastructure.Sms;

namespace OqTish.Api.Endpoints;

/// <summary>
/// /api/dev — mapped only in Development (404 everywhere else, proven by a test).
/// </summary>
public static class DevEndpoints
{
    public sealed record DevSmsDto(string Text, DateTimeOffset SentAt);

    public sealed record SeedResultDto(IReadOnlyList<string> Contributors);

    public sealed record DevTelegramMessageDto(long ChatId, string Text, JsonElement? Buttons, DateTimeOffset SentAt, string Origin);

    /// <summary>A fake private-chat message to the bot: <c>text</c> such as <c>/start &lt;token&gt;</c>, <c>/appointments</c>.</summary>
    public sealed record DevTelegramSimulateCommand(long? TelegramUserId, string? Text, string? LanguageCode);

    public sealed record DevTelegramSimulateResult(BotOutcome Outcome);

    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment())
        {
            return app;
        }

        var dev = app.MapGroup("/api/dev").WithTags("Development");

        // This process's in-memory mailbox (OTP codes) plus SMS captured by other processes (the Worker's SMS fallback).
        dev.MapGet("/sms", async (string? phone, FakeSmsMailbox mailbox, OqTishDbContext db, IDevMessageSink sink, HttpContext http, CancellationToken ct) =>
            {
                if (!PhoneNumber.TryNormalize(phone, out var e164))
                {
                    return ProblemResults.ToResult(Error.InvalidField("phone", "Enter an Uzbekistan mobile number."), http);
                }

                var origin = sink.Origin;
                var others = await db.DevCapturedMessages.AsNoTracking()
                    .Where(m => m.Channel == "Sms" && m.Recipient == e164 && m.Origin != origin)
                    .OrderByDescending(m => m.CreatedAt).Take(50)
                    .Select(m => new DevSmsDto(m.Text, m.CreatedAt))
                    .ToListAsync(ct);
                return Results.Ok(mailbox.For(e164).Select(m => new DevSmsDto(m.Text, m.SentAt)).Concat(others).OrderByDescending(m => m.SentAt).ToList());
            })
            .AllowAnonymous()
            .WithName("DevSms").WithSummary("Messages captured by the Fake SMS sender (Development only)")
            .Produces<List<DevSmsDto>>().ProducesProblem(400);

        dev.MapGet("/telegram", async (long? chatId, OqTishDbContext db, CancellationToken ct) =>
            {
                var query = db.DevCapturedMessages.AsNoTracking().Where(m => m.Channel == "Telegram");
                if (chatId is { } id)
                {
                    var recipient = id.ToString(CultureInfo.InvariantCulture);
                    query = query.Where(m => m.Recipient == recipient);
                }

                var rows = await query.OrderByDescending(m => m.CreatedAt).Take(100).ToListAsync(ct);
                return TypedResults.Ok(rows.Select(m => new DevTelegramMessageDto(
                    long.TryParse(m.Recipient, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var chat) ? chat : 0,
                    m.Text,
                    m.Buttons is null ? null : JsonSerializer.Deserialize<JsonElement>(m.Buttons),
                    m.CreatedAt,
                    m.Origin)).ToList());
            })
            .AllowAnonymous()
            .WithName("DevTelegram").WithSummary("Messages the Fake Telegram client would have sent, newest first (Development only)")
            .Produces<List<DevTelegramMessageDto>>();

        dev.MapPost("/telegram/simulate", async (DevTelegramSimulateCommand body, TelegramBotService bot, HttpContext http, CancellationToken ct) =>
            {
                if (body.TelegramUserId is not { } userId || userId <= 0 || string.IsNullOrWhiteSpace(body.Text))
                {
                    return ProblemResults.ToResult(Error.InvalidField("text", "telegramUserId (> 0) and text are required."), http);
                }

                var update = new TelegramUpdate(0, new TelegramIncomingMessage(0, userId, "private", userId, false, body.LanguageCode, body.Text));
                return Results.Ok(new DevTelegramSimulateResult(await bot.HandleAsync(update, ct)));
            })
            .AllowAnonymous()
            .WithName("DevTelegramSimulate").WithSummary("Feed a private-chat message to the bot as if Telegram sent it (Development only)")
            .Produces<DevTelegramSimulateResult>().ProducesProblem(400);

        dev.MapPost("/seed", async (SeedRunner seed, CancellationToken ct) =>
                TypedResults.Ok(new SeedResultDto([.. (await seed.RunAsync(ct)).Select(s => s.Name)])))
            .AllowAnonymous()
            .WithName("DevSeed").WithSummary("Idempotently load fictional demo data (Development only)")
            .Produces<SeedResultDto>();

        return app;
    }
}
