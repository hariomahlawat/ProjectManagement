using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ProjectManagement.Helpers;

public static class HttpContextExtensions
{
    public static IActionResult SetSuccess(this HttpContext context)
    {
        return context.SetSuccess(new { ok = true });
    }

    public static IActionResult SetSuccess(this HttpContext context, object value)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return new JsonResult(value)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    public static IActionResult SetStatusCode(this HttpContext context, int statusCode, object? value)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return new ObjectResult(value)
        {
            StatusCode = statusCode
        };
    }

    public static IActionResult SetInternalServerError(this HttpContext context)
    {
        return context.SetStatusCode(StatusCodes.Status500InternalServerError, new { ok = false });
    }
}
