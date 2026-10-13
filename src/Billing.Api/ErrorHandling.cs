using Billing.Application;
using Billing.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Billing.Api;

/// <summary>Maps domain and persistence errors to RFC 7807 problem responses.</summary>
public class BillingExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Modified by someone else"),
            DbUpdateException => (StatusCodes.Status409Conflict, "Conflicts with existing data"),
            _ => (0, ""),
        };
        if (status == 0) return false;

        http.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception is DbUpdateException ? null : exception.Message,
            },
        });
    }
}
