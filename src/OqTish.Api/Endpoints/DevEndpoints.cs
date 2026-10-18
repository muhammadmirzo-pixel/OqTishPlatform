using OqTish.Api.Common;
using OqTish.Application.Common;
using OqTish.Application.Common.Seeding;
using OqTish.Application.Identity;
using OqTish.Infrastructure.Sms;

namespace OqTish.Api.Endpoints;

/// <summary>
/// /api/dev — mapped only in Development (404 everywhere else, proven by a test).
/// </summary>
public static class DevEndpoints
{
    public sealed record DevSmsDto(string Text, DateTimeOffset SentAt);

    public sealed record SeedResultDto(IReadOnlyList<string> Contributors);

    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment())
        {
            return app;
        }

        var dev = app.MapGroup("/api/dev").WithTags("Development");

        dev.MapGet("/sms", (string? phone, FakeSmsMailbox mailbox, HttpContext http) =>
                PhoneNumber.TryNormalize(phone, out var e164)
                    ? Results.Ok(mailbox.For(e164).Select(m => new DevSmsDto(m.Text, m.SentAt)).ToList())
                    : ProblemResults.ToResult(Error.InvalidField("phone", "Enter an Uzbekistan mobile number."), http))
            .AllowAnonymous()
            .WithName("DevSms").WithSummary("Messages captured by the Fake SMS sender (Development only)")
            .Produces<List<DevSmsDto>>().ProducesProblem(400);

        dev.MapPost("/seed", async (SeedRunner seed, CancellationToken ct) =>
                TypedResults.Ok(new SeedResultDto([.. (await seed.RunAsync(ct)).Select(s => s.Name)])))
            .AllowAnonymous()
            .WithName("DevSeed").WithSummary("Idempotently load fictional demo data (Development only)")
            .Produces<SeedResultDto>();

        return app;
    }
}
