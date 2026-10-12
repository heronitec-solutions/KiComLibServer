namespace KiComLibBackend.Services
{
    public static class SessionCookies
    {
        public const string Name = "kicomlib_session";

        public const string CsrfHeader = "X-KiComLib-Request";

        public static void Set(HttpResponse response, HttpRequest request, string token, DateTime expiresAt)
        {
            response.Cookies.Append(Name, token, Options(request, expiresAt));
        }

        public static void Clear(HttpResponse response, HttpRequest request)
        {
            response.Cookies.Delete(Name, Options(request, expiresAt: null));
        }

        public static bool HasCsrfHeader(HttpRequest request)
        {
            return request.Headers.TryGetValue(CsrfHeader, out Microsoft.Extensions.Primitives.StringValues values)
                && values.ToString() == "1";
        }

        private static CookieOptions Options(HttpRequest request, DateTime? expiresAt)
        {
            return new CookieOptions
            {
                HttpOnly = true,
                Secure = request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                Expires = expiresAt == null ? null : new DateTimeOffset(DateTime.SpecifyKind(expiresAt.Value, DateTimeKind.Utc)),
                IsEssential = true
            };
        }
    }
}
