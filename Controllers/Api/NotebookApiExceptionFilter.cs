using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Controllers.Api;

// SECTION: Notebook API exception translation
public sealed class NotebookApiExceptionFilter : IAsyncExceptionFilter
{
    public Task OnExceptionAsync(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            KeyNotFoundException => new NotFoundObjectResult(new { code = "notebook_not_found", message = "The note could not be found." }),
            ArgumentException => new BadRequestObjectResult(new { code = "notebook_validation_failed", message = "The notebook request is invalid." }),
            NotebookConcurrencyException ex => new ConflictObjectResult(new
            {
                code = "notebook_concurrency_conflict",
                message = "This note was changed elsewhere. Reload the latest version before continuing.",
                currentVersion = ex.CurrentVersion.ToString("N")
            }),
            DbUpdateConcurrencyException => new ConflictObjectResult(new
            {
                code = "notebook_concurrency_conflict",
                message = "This note was changed elsewhere. Reload the latest version before continuing."
            }),
            _ => null
        };

        if (context.Result is not null)
        {
            context.ExceptionHandled = true;
        }

        return Task.CompletedTask;
    }
}
