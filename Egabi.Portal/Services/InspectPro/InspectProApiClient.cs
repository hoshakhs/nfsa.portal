// ============================================================
// FILE: Services/InspectPro/InspectProApiClient.cs
// The ONLY way the portal talks to the egabi Platform (InspectPro).
// Server-to-server: the browser never calls InspectPro directly.
// Every call carries:
//   X-Portal-Key      the portal's shared secret
//   X-Client-IP       the end user's IP (for rate limiting)
//   X-Correlation-Id  to trace the call in both systems' logs
//   Authorization     the applicant's token (when signed in)
// ============================================================
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Egabi.Portal.Services.InspectPro
{
    public class InspectProApiClient
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _http;
        private readonly IHttpContextAccessor _ctx;
        private readonly ILogger<InspectProApiClient> _logger;
        private readonly InspectProApiOptions _options;

        public InspectProApiClient(
            HttpClient http,
            IOptions<InspectProApiOptions> options,
            IHttpContextAccessor ctx,
            ILogger<InspectProApiClient> logger)
        {
            _http = http;
            _ctx = ctx;
            _logger = logger;
            _options = options.Value;
        }

        // ── Auth ───────────────────────────────────────────────
        public Task<ApiResult<AuthResponse>> LoginAsync(string login, string password) =>
            SendAsync<AuthResponse>(HttpMethod.Post, "api/public/v1/auth/login", null,
                JsonBody(new { login, password }));

        public Task<ApiResult<AuthResponse>> RegisterAsync(object request) =>
            SendAsync<AuthResponse>(HttpMethod.Post, "api/public/v1/auth/register", null, JsonBody(request));

        // ── Facilities ─────────────────────────────────────────
        public Task<ApiResult<List<FacilitySummary>>> GetMyFacilitiesAsync(string token) =>
            SendAsync<List<FacilitySummary>>(HttpMethod.Get, "api/public/v1/facilities", token);

        public Task<ApiResult<FacilityDetails>> GetFacilityAsync(string token, int id) =>
            SendAsync<FacilityDetails>(HttpMethod.Get, $"api/public/v1/facilities/{id}", token);

        public Task<ApiResult<JsonElement>> CreateFacilityAsync(string token, string rawJson) =>
            SendAsync<JsonElement>(HttpMethod.Post, "api/public/v1/facilities", token,
                new StringContent(rawJson, Encoding.UTF8, "application/json"));

        public async Task<ApiResult<JsonElement>> UploadDocumentAsync(
            string token, int facilityId, string documentTypeCode, IFormFile file)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(documentTypeCode), "documentTypeCode");

            var stream = new StreamContent(file.OpenReadStream());
            stream.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            form.Add(stream, "file", Path.GetFileName(file.FileName));

            return await SendAsync<JsonElement>(HttpMethod.Post,
                $"api/public/v1/facilities/{facilityId}/documents", token, form);
        }

        public Task<ApiResult<JsonElement>> SubmitFacilityAsync(string token, int facilityId) =>
            SendAsync<JsonElement>(HttpMethod.Post, $"api/public/v1/facilities/{facilityId}/submit", token);

        // ── Lookups (returned as-is to the browser) ────────────
        public Task<ApiResult<JsonElement>> GetLookupAsync(string token, string name, string? queryString) =>
            SendAsync<JsonElement>(HttpMethod.Get, $"api/public/v1/lookups/{name}{queryString}", token);

        // ── Service Catalogue ──────────────────────────────────
        public Task<ApiResult<List<ServiceSummary>>> GetServicesAsync() =>
            SendAsync<List<ServiceSummary>>(HttpMethod.Get, "api/public/v1/services", null);

        public Task<ApiResult<ServiceDetails>> GetServiceAsync(string token, string code) =>
            SendAsync<ServiceDetails>(HttpMethod.Get, $"api/public/v1/services/{Uri.EscapeDataString(code)}", token);

        public Task<ApiResult<JsonElement>> SubmitServiceRequestAsync(string token, string code, string rawJson) =>
            SendAsync<JsonElement>(HttpMethod.Post, $"api/public/v1/services/{Uri.EscapeDataString(code)}/requests", token,
                new StringContent(rawJson, Encoding.UTF8, "application/json"));

        public Task<ApiResult<List<ServiceRequestItem>>> GetMyServiceRequestsAsync(string token) =>
                SendAsync<List<ServiceRequestItem>>(HttpMethod.Get, "api/public/v1/services/requests", token);

        // Batch 7: a returned request — read it, then correct and resubmit
        public Task<ApiResult<ServiceRequestDetails>> GetMyServiceRequestAsync(string token, string reference) =>
            SendAsync<ServiceRequestDetails>(HttpMethod.Get,
                $"api/public/v1/services/requests/{Uri.EscapeDataString(reference)}", token);

        public Task<ApiResult<JsonElement>> ResubmitServiceRequestAsync(string token, string reference, string rawJson) =>
            SendAsync<JsonElement>(HttpMethod.Post,
                $"api/public/v1/services/requests/{Uri.EscapeDataString(reference)}/resubmit", token,
                new StringContent(rawJson, Encoding.UTF8, "application/json"));

        /// <summary>The Form Builder renderer script (raw JavaScript text).</summary>
        public Task<ApiResult<string>> GetFormRendererAsync() =>
            SendAsync<string>(HttpMethod.Get, "api/public/v1/services/renderer.js", null);


        /// <summary>
        /// Calls the form makes while it is filled (options / lookup / rules / upload).
        /// The answer is returned as-is, in the renderer's own { success, ... } shape.
        /// </summary>
        public Task<ApiResult<JsonElement>> ServiceFormGetAsync(string token, string code, string relativePath) =>
            SendAsync<JsonElement>(HttpMethod.Get,
                $"api/public/v1/services/{Uri.EscapeDataString(code)}/{relativePath}", token);

        public Task<ApiResult<JsonElement>> ServiceFormRuleAsync(string token, string code, string rawJson) =>
            SendAsync<JsonElement>(HttpMethod.Post, $"api/public/v1/services/{Uri.EscapeDataString(code)}/rules/evaluate", token,
                new StringContent(rawJson, Encoding.UTF8, "application/json"));

        public async Task<ApiResult<JsonElement>> ServiceFormUploadAsync(
            string token, string code, string fieldKey, IReadOnlyList<IFormFile> files)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(fieldKey), "fieldKey");
            foreach (var file in files)
            {
                var part = new StreamContent(file.OpenReadStream());
                part.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
                form.Add(part, "files", Path.GetFileName(file.FileName));
            }
            return await SendAsync<JsonElement>(HttpMethod.Post,
                $"api/public/v1/services/{Uri.EscapeDataString(code)}/upload", token, form);
        }

        // ── Certificates (NFSA N4) ─────────────────────────────
        public Task<ApiResult<List<CertificateItem>>> GetMyCertificatesAsync(string token) =>
            SendAsync<List<CertificateItem>>(HttpMethod.Get, "api/public/v1/certificates", token);

        /// <summary>Public check by verification code — no sign-in needed.</summary>
        public Task<ApiResult<CertificateVerification>> VerifyCertificateAsync(string code) =>
            SendAsync<CertificateVerification>(HttpMethod.Get,
                $"api/public/v1/certificates/verify/{Uri.EscapeDataString(code)}", null);

        /// <summary>The PDF of one of my certificates (bytes), or null when not found / not mine.</summary>
        public async Task<(byte[]? Pdf, int Status)> GetCertificatePdfAsync(string token, string number)
        {
            var path = $"api/public/v1/certificates/{Uri.EscapeDataString(number)}/pdf";
            if (string.IsNullOrWhiteSpace(_options.PortalClientKey))
            {
                _logger.LogError("InspectProApi:PortalClientKey is not set — add it to User Secrets or appsettings.");
                return (null, 503);
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Add("X-Portal-Key", _options.PortalClientKey);
            req.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
            var ip = _ctx.HttpContext?.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(ip)) req.Headers.Add("X-Client-IP", ip);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                using var res = await _http.SendAsync(req);
                if (!res.IsSuccessStatusCode) return (null, (int)res.StatusCode);
                return (await res.Content.ReadAsByteArrayAsync(), 200);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InspectPro API unreachable: GET {Path}", path);
                return (null, 503);
            }
        }

        // ════════════════════════════════════════════════════
        // CORE
        // ════════════════════════════════════════════════════
        private static StringContent JsonBody(object body) =>
            new(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, string? token, HttpContent? body = null)
        {
            var correlationId = Guid.NewGuid().ToString("N");

            if (string.IsNullOrWhiteSpace(_options.PortalClientKey))
            {
                _logger.LogError("InspectProApi:PortalClientKey is not set — add it to User Secrets or appsettings.");
                return new ApiResult<T>
                {
                    Ok = false,
                    Status = 503,
                    Message = "The service is temporarily unavailable. Please try again later.",
                    CorrelationId = correlationId
                };
            }

            using var req = new HttpRequestMessage(method, path) { Content = body };
            req.Headers.Add("X-Portal-Key", _options.PortalClientKey);
            req.Headers.Add("X-Correlation-Id", correlationId);

            var ip = _ctx.HttpContext?.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(ip)) req.Headers.Add("X-Client-IP", ip);

            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(req);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InspectPro API unreachable: {Method} {Path} cid={Cid}", method, path, correlationId);
                return new ApiResult<T>
                {
                    Ok = false,
                    Status = 503,
                    Message = "The service is temporarily unavailable. Please try again later.",
                    CorrelationId = correlationId
                };
            }

            using (res)
            {
                var text = await res.Content.ReadAsStringAsync();
                var status = (int)res.StatusCode;

                if (res.IsSuccessStatusCode)
                {
                    T? data = default;
                    if (typeof(T) == typeof(string))
                        data = (T)(object)text;             // raw text (e.g. a script)
                    else if (!string.IsNullOrWhiteSpace(text))
                        data = JsonSerializer.Deserialize<T>(text, Json);

                    return new ApiResult<T> { Ok = true, Status = status, Data = data, CorrelationId = correlationId };
                }

                // Error: read the standard ProblemDetails shape { title, errors, correlationId }
                string? message = null;
                var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                            message = t.GetString();

                        if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                            foreach (var p in e.EnumerateObject())
                                errors[p.Name] = p.Value.ValueKind == JsonValueKind.Array
                                    ? p.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                                    : new[] { p.Value.ToString() };
                    }
                }
                catch (JsonException)
                {
                    // not JSON — keep the generic message below
                }

                if (status >= 500)
                    _logger.LogWarning("InspectPro API error {Status} {Method} {Path} cid={Cid}", status, method, path, correlationId);

                return new ApiResult<T>
                {
                    Ok = false,
                    Status = status,
                    Message = message ?? (status == 429
                        ? "Too many attempts. Please wait a minute and try again."
                        : "The request could not be completed."),
                    Errors = errors,
                    CorrelationId = correlationId
                };
            }
        }
    }
}
