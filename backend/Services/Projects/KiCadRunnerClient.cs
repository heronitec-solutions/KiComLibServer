using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KiComLibBackend.Services
{
    public sealed class OutputBomLine
    {
        public string Reference { get; init; } = "";

        public string Value { get; init; } = "";

        public string Footprint { get; init; } = "";

        public string Mpn { get; init; } = "";

        public int Quantity { get; init; } = 1;
    }

    public interface IKiCadRunnerClient
    {
        Task<string> AnalyzeAsync(byte[] projectTar, string projectFile, string? variant, CancellationToken cancellationToken);

        Task<byte[]> RenderAsync(byte[] projectTar, string projectFile, CancellationToken cancellationToken);

        Task<byte[]> VisualDiffAsync(byte[] baseTar, byte[] targetTar, string projectFile, CancellationToken cancellationToken);

        Task<byte[]> OutputsAsync(
            byte[] projectTar,
            string projectFile,
            IReadOnlyList<string> outputs,
            string? variant,
            IReadOnlyList<OutputBomLine>? bom,
            byte[]? modelsZip,
            CancellationToken cancellationToken);
    }

    public sealed class RunnerCallException : Exception
    {
        public RunnerCallException(string code, int statusCode, string message, bool retryable)
            : base(message)
        {
            Code = code;
            StatusCode = statusCode;
            Retryable = retryable;
        }

        public string Code { get; }

        public int StatusCode { get; }

        public bool Retryable { get; }
    }

    public sealed class KiCadRunnerClient : IKiCadRunnerClient
    {
        public const string HttpClientName = "kicad-runner";

        private readonly IHttpClientFactory _http;

        public KiCadRunnerClient(IHttpClientFactory http)
        {
            _http = http;
        }

        public Task<string> AnalyzeAsync(byte[] projectTar, string projectFile, string? variant, CancellationToken cancellationToken)
        {
            string spec = JsonSerializer.Serialize(new
            {
                projectFile,
                variant = string.IsNullOrWhiteSpace(variant) ? null : variant
            });
            return PostTextAsync("/v1/analyze", form =>
            {
                form.Add(TarContent(projectTar), "project", "project.tar");
                form.Add(JsonContent(spec), "spec", "spec.json");
            }, cancellationToken);
        }

        public async Task<byte[]> RenderAsync(byte[] projectTar, string projectFile, CancellationToken cancellationToken)
        {
            string spec = JsonSerializer.Serialize(new
            {
                projectFile,
                schematic = true,
                board = new[] { "front", "back" }
            });
            return await PostBytesAsync("/v1/render", form =>
            {
                form.Add(TarContent(projectTar), "project", "project.tar");
                form.Add(JsonContent(spec), "spec", "spec.json");
            }, cancellationToken);
        }

        public async Task<byte[]> VisualDiffAsync(byte[] baseTar, byte[] targetTar, string projectFile, CancellationToken cancellationToken)
        {
            string spec = JsonSerializer.Serialize(new
            {
                projectFile,
                categories = new[] { "parts", "wiring", "graphics", "routing", "zones" },
                board = new[] { "front", "back" }
            });
            return await PostBytesAsync("/v1/visual-diff", form =>
            {
                form.Add(TarContent(baseTar), "base", "base.tar");
                form.Add(TarContent(targetTar), "target", "target.tar");
                form.Add(JsonContent(spec), "spec", "spec.json");
            }, cancellationToken);
        }

        public Task<byte[]> OutputsAsync(
            byte[] projectTar,
            string projectFile,
            IReadOnlyList<string> outputs,
            string? variant,
            IReadOnlyList<OutputBomLine>? bom,
            byte[]? modelsZip,
            CancellationToken cancellationToken)
        {
            Dictionary<string, object?> spec = new()
            {
                ["projectFile"] = projectFile,
                ["outputs"] = outputs
            };
            if (!string.IsNullOrWhiteSpace(variant))
            {
                spec["variant"] = variant;
            }

            if (bom != null)
            {
                spec["bom"] = bom.Select(line => new
                {
                    reference = line.Reference,
                    value = line.Value,
                    footprint = line.Footprint,
                    mpn = line.Mpn,
                    quantity = line.Quantity
                }).ToList();
            }

            string json = JsonSerializer.Serialize(spec);
            return PostBytesAsync("/v1/outputs", form =>
            {
                form.Add(TarContent(projectTar), "project", "project.tar");
                form.Add(JsonContent(json), "spec", "spec.json");
                if (modelsZip != null && modelsZip.Length > 0)
                {
                    ByteArrayContent models = new(modelsZip);
                    models.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                    form.Add(models, "models", "models.zip");
                }
            }, cancellationToken);
        }

        private async Task<string> PostTextAsync(string path, Action<MultipartFormDataContent> fill, CancellationToken cancellationToken)
        {
            byte[] body = await PostBytesAsync(path, fill, cancellationToken);
            return Encoding.UTF8.GetString(body);
        }

        private async Task<byte[]> PostBytesAsync(string path, Action<MultipartFormDataContent> fill, CancellationToken cancellationToken)
        {
            string baseUrl = Environment.GetEnvironmentVariable("KICAD_RUNNER_URL") ?? "";
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://kicad-runner:8080";
            }

            baseUrl = baseUrl.TrimEnd('/');
            HttpClient client = _http.CreateClient(HttpClientName);
            using HttpRequestMessage request = new(HttpMethod.Post, baseUrl + path);
            string? token = Environment.GetEnvironmentVariable("KICAD_RUNNER_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using MultipartFormDataContent form = new();
            fill(form);
            request.Content = form;
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new RunnerCallException("timeout", 504, "The KiCad runner timed out.", retryable: true);
            }
            catch (HttpRequestException)
            {
                throw new RunnerCallException("failed", 503, "The KiCad runner is not reachable.", retryable: true);
            }

            using (response)
            {
                byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                throw ParseFailure(response, body);
            }
        }

        private static RunnerCallException ParseFailure(HttpResponseMessage response, byte[] body)
        {
            string text = Encoding.UTF8.GetString(body);
            string code = "failed";
            string error = "The KiCad runner failed.";
            try
            {
                using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
                if (doc.RootElement.TryGetProperty("code", out JsonElement codeElement) && codeElement.ValueKind == JsonValueKind.String)
                {
                    code = codeElement.GetString() ?? code;
                }

                if (doc.RootElement.TryGetProperty("error", out JsonElement errorElement) && errorElement.ValueKind == JsonValueKind.String)
                {
                    string? message = errorElement.GetString();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        error = message.Trim();
                    }
                }
            }
            catch (JsonException)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    error = text.Trim();
                }
            }

            if (error.Length > 500)
            {
                error = error[..500];
            }

            int status = (int)response.StatusCode;
            if (status == 504)
            {
                code = "timeout";
                if (string.IsNullOrWhiteSpace(error) || error == "The KiCad runner failed.")
                {
                    error = "The KiCad runner timed out.";
                }
            }

            bool retryable = status >= 500 || code == "timeout";
            return new RunnerCallException(code, status, error, retryable);
        }

        private static ByteArrayContent TarContent(byte[] tar)
        {
            ByteArrayContent content = new(tar);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-tar");
            return content;
        }

        private static ByteArrayContent JsonContent(string json)
        {
            ByteArrayContent content = new(Encoding.UTF8.GetBytes(json));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return content;
        }
    }
}
