using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using OqTish.Application.Common;

namespace OqTish.Api.Common;

/// <summary>Maps <see cref="Error"/> to RFC 7807 ProblemDetails with <c>code</c>, <c>traceId</c> and optional <c>errors</c>.</summary>
public static class ProblemResults
{
    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.RuleViolation => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static ProblemDetails Create(Error error, HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(http);
        var status = StatusFor(error.Kind);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrase(status),
            Detail = error.Message,
            Type = $"https://httpstatuses.io/{status}",
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = http.TraceIdentifier;
        if (error.FieldErrors is { Count: > 0 } fields)
        {
            problem.Extensions["errors"] = fields;
        }

        return problem;
    }

    /// <summary>The IResult for an error; sets Retry-After for rate-limit errors.</summary>
    public static IResult ToResult(Error error, HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(http);
        if (error.RetryAfter is { } retry)
        {
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        return TypedResults.Problem(Create(error, http));
    }

    /// <summary>Writes the problem directly (middleware, authentication handlers, rate limiter).</summary>
    public static Task WriteAsync(HttpContext http, Error error)
    {
        ArgumentNullException.ThrowIfNull(http);
        return ToResult(error, http).ExecuteAsync(http);
    }

    public static IResult ToHttp<T>(this Result<T> result, HttpContext http, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        return result.IsSuccess ? onSuccess(result.Value) : ToResult(result.Error, http);
    }

    public static IResult ToHttp(this Result result, HttpContext http, Func<IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        return result.IsSuccess ? onSuccess() : ToResult(result.Error!, http);
    }

    /// <summary>Default <c>code</c> for problems produced by the framework (404 route, 405, 500 …).</summary>
    public static string DefaultCode(int status) => status switch
    {
        400 => CommonErrors.ValidationCode,
        401 => "auth.unauthenticated",
        403 => "common.forbidden",
        404 => "common.not_found",
        405 => "common.method_not_allowed",
        413 => "common.payload_too_large",
        415 => "common.unsupported_media_type",
        429 => "auth.rate_limited",
        _ => status >= 500 ? "common.internal" : "common.error",
    };

    private static string ReasonPhrase(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        _ => "Error",
    };
}
