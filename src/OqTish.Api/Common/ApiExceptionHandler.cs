using Microsoft.AspNetCore.Diagnostics;
using OqTish.Application.Common;
using OqTish.Application.Common.Json;

namespace OqTish.Api.Common;

/// <summary>
/// Maps request-binding failures (malformed JSON, wrong types, invalid money) to 400 ProblemDetails:
/// <c>finance.amount_invalid</c> for <see cref="MoneyFormatException"/>, otherwise <c>common.validation</c>.
/// Other exceptions fall through to the default 500 handler (no internals in the response).
/// </summary>
internal sealed class ApiExceptionHandler : IExceptionHandler
{
    private static readonly Error InvalidAmount = Error.Validation(MoneyFormatException.ErrorCode, "Amount must be a positive number with at most 2 decimals.");
    private static readonly Error InvalidBody = Error.Validation(CommonErrors.ValidationCode, "The request is malformed.");

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException)
        {
            return false;
        }

        var error = InvalidBody;
        for (var ex = exception.InnerException; ex is not null; ex = ex.InnerException)
        {
            if (ex is MoneyFormatException)
            {
                error = InvalidAmount;
                break;
            }
        }

        await ProblemResults.WriteAsync(httpContext, error);
        return true;
    }
}
