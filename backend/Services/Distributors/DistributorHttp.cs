using System.Net.Http.Headers;

namespace KiComLibBackend.Services.Distributors
{
    internal static class DistributorHttp
    {
        public static Task<string> SendAsync(
            IHttpClientFactory http,
            string clientName,
            string providerId,
            HttpRequestMessage request,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            return SendAsync(http, clientName, providerId, request, logger, null, cancellationToken);
        }

        public static async Task<string> SendAsync(
            IHttpClientFactory http,
            string clientName,
            string providerId,
            HttpRequestMessage request,
            ILogger logger,
            Func<int, string, string?>? describeFailure,
            CancellationToken cancellationToken)
        {
            if (!request.Headers.Accept.Any())
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }

            HttpClient client = http.CreateClient(clientName);
            string? url = request.RequestUri?.ToString();
            try
            {
                using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    string? reason = null;
                    try
                    {
                        reason = describeFailure?.Invoke((int)response.StatusCode, body);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Distributor {Provider} error response could not be read", providerId);
                    }

                    logger.LogWarning(
                        "Distributor {Provider} request failed with status {Status} for {Url}: {Detail}",
                        providerId,
                        (int)response.StatusCode,
                        DistributorLog.Sanitize(url),
                        DistributorLog.Sanitize(reason));
                    throw new DistributorUpstreamException(
                        providerId,
                        string.IsNullOrWhiteSpace(reason) ? "The distributor request failed" : reason.Trim());
                }

                return body;
            }
            catch (DistributorUpstreamException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Distributor {Provider} request failed for {Url}: {Detail}",
                    providerId,
                    DistributorLog.Sanitize(url),
                    DistributorLog.Sanitize(exception.Message));
                throw new DistributorUpstreamException(providerId, "The distributor request failed");
            }
        }
    }
}
