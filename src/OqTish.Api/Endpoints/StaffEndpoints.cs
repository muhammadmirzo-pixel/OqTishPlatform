using OqTish.Api.Common;
using OqTish.Application.Staff;

namespace OqTish.Api.Endpoints;

/// <summary>/api/me/invitations — the invitee's side of staff invitations (docs/API.md §1).</summary>
public static class StaffEndpoints
{
    public static IEndpointRouteBuilder MapMyInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me/invitations").WithTags("Me").RequireAuthorization();

        me.MapGet("", async (MyInvitationHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.ListAsync(ct)).ToHttp(http, TypedResults.Ok))
            .WithName("ListMyInvitations").WithSummary("Open invitations addressed to the caller's verified phone")
            .Produces<IReadOnlyList<MyInvitationDto>>().ProducesProblem(401);

        me.MapPost("/{invitationId:guid}/accept", async (Guid invitationId, MyInvitationHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.AcceptAsync(invitationId, ct)).ToHttp(http, TypedResults.NoContent))
            .WithName("AcceptInvitation").WithSummary("Accept an invitation (phone must match; creates the membership)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

        return app;
    }
}
