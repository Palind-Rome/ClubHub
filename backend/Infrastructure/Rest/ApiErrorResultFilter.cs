using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ClubHub.Api.Infrastructure.Rest;

public sealed class ApiErrorResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        var (statusCode, payload) = context.Result switch
        {
            ObjectResult objectResult when objectResult.StatusCode is >= 400 =>
                (objectResult.StatusCode.Value, objectResult.Value),
            StatusCodeResult statusResult when statusResult.StatusCode >= 400 =>
                (statusResult.StatusCode, null),
            _ => (0, null)
        };

        if (statusCode >= 400)
        {
            context.Result = new ObjectResult(ApiErrorFactory.Create(statusCode, payload))
            {
                StatusCode = statusCode
            };
        }

        await next();
    }
}
