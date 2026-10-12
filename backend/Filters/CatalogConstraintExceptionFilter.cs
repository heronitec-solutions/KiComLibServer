using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KiComLibBackend.Filters
{
    /// <summary>
    /// Returns database constraint failures as field errors instead of an unstructured 500.
    /// </summary>
    public sealed class CatalogConstraintExceptionFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            Dictionary<string, string[]>? errors = CatalogConstraintErrors.FromException(context.Exception);
            if (errors == null || errors.Count == 0)
            {
                return;
            }

            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            });
            context.ExceptionHandled = true;
        }
    }
}
