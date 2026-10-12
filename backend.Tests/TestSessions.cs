using System.Net.Http.Headers;
using KiComLibBackend.Services;

namespace KiComLibBackend.Tests;

internal static class TestSessions
{
    public static string UseBearer(HttpClient client, HttpResponseMessage login)
    {
        string token = ReadToken(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return token;
    }

    public static string ReadToken(HttpResponseMessage login)
    {
        if (login.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
        {
            string prefix = SessionCookies.Name + "=";
            foreach (string cookie in cookies)
            {
                if (!cookie.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                int end = cookie.IndexOf(';');
                return end < 0 ? cookie[prefix.Length..] : cookie[prefix.Length..end];
            }
        }

        throw new InvalidOperationException("Sign-in did not set a session cookie.");
    }
}
