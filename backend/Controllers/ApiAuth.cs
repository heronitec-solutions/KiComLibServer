using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace KiComLibBackend.Controllers
{
    internal static class ApiAuth
    {
        public static IActionResult? RequireAdmin(HttpContext http)
        {
            CallerContext caller = CallerContext.Current(http);
            if (caller.TokenRejected || !caller.IsAuthenticated)
            {
                return new UnauthorizedObjectResult(new { error = caller.TokenRejected ? "Invalid API token" : "Authentication required" });
            }

            if (!caller.IsAdmin)
            {
                return new ObjectResult(new { error = "Administrator permission required" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            return null;
        }

        public static string? WebhookUrl(Guid repositoryUuid)
        {
            string path = "/api/hooks/git/" + repositoryUuid.ToString();
            string? baseUrl = Environment.GetEnvironmentVariable("PUBLIC_BASE_URL");
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return path;
            }

            return baseUrl.TrimEnd('/') + path;
        }
    }
}
