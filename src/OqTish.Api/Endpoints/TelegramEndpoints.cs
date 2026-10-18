using OqTish.Application.Identity;
using OqTish.Application.Identity.Handlers;

namespace OqTish.Api.Endpoints;

/// <summary>/api/telegram — public Telegram configuration. The webhook (BE4) is mapped by the Notifications module.</summary>
public static class TelegramEndpoints
{
    public static IEndpointRouteBuilder MapTelegramEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/telegram/config", (TelegramConfigQuery query) => TypedResults.Ok(query.Handle()))
            .AllowAnonymous()
            .WithTags("Telegram")
            .WithName("GetTelegramConfig").WithSummary("{ enabled, botUsername, miniAppUrl } for the UI")
            .Produces<TelegramConfigDto>();
        return app;
    }
}
