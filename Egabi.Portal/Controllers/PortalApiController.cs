// ============================================================
// FILE: Controllers/PortalApiController.cs
// Endpoints called by the portal's own pages (JavaScript).
// Each one forwards to the egabi Platform Public API through
// InspectProApiClient — the browser never talks to InspectPro.
//
//   POST /portal/api/account/login
//   POST /portal/api/account/register
//   POST /portal/account/logout              (normal form post)
//   GET  /portal/api/lookups/{name}
//   POST /portal/api/facilities
//   POST /portal/api/facilities/{id}/documents
//   POST /portal/api/facilities/{id}/submit
//
//   Service Catalogue (batch 5):
//   GET  /portal/forms/renderer.js                       Form Builder renderer (from the platform)
//   POST /portal/api/services/{code}/requests            submit a service request
//   GET  /portal/api/services/{code}/options/{id}        ┐
//   GET  /portal/api/services/{code}/data/{e}/lookup     │ called by the form while
//   POST /portal/api/services/{code}/rules/evaluate      │ it is filled
//   POST /portal/api/services/{code}/upload              ┘
//   POST /portal/api/services/requests/{ref}/resubmit    correct a returned request (batch 7)
//
// JSON reply shape:
//   { ok: true,  redirect?: "...", data?: {...}, message?: "..." }
//   { ok: false, message: "...", errors?: { field: ["..."] }, redirect?: "..." }
// ============================================================
using System.Text.Json;
using System.Text.RegularExpressions;
using Egabi.Portal.Services.Applicant;
using Egabi.Portal.Services.InspectPro;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Egabi.Portal.Controllers
{
    [Route("portal")]
    public class PortalApiController : Controller
    {
        private static readonly HashSet<string> AllowedLookups = new(StringComparer.OrdinalIgnoreCase)
        {
            "sectors", "facility-types", "legal-forms", "license-types", "issuing-authorities",
            "countries", "governorates", "administrative-areas", "districts",
            "document-requirements", "facility-form-config"
        };

        private static readonly Regex ServiceCode = new("^[A-Za-z0-9_-]{2,50}$", RegexOptions.Compiled);
        private static readonly Regex EntityName = new("^[A-Za-z0-9_]{1,100}$", RegexOptions.Compiled);
        private static readonly Regex RequestReference = new("^[A-Za-z0-9-]{3,30}$", RegexOptions.Compiled);

        private readonly InspectProApiClient _api;
        private readonly IMemoryCache _cache;

        public PortalApiController(InspectProApiClient api, IMemoryCache cache)
        {
            _api = api;
            _cache = cache;
        }

        // These endpoints are not Umbraco pages, so the page language is sent by the
        // browser (header "X-Portal-Lang: ar|en", or form field "lang" for sign-out).
        private bool Ar =>
            string.Equals(Request.Headers["X-Portal-Lang"].ToString(), "ar", StringComparison.OrdinalIgnoreCase) ||
            (Request.HasFormContentType && string.Equals(Request.Form["lang"].ToString(), "ar", StringComparison.OrdinalIgnoreCase));

        private string U(string path) => (Ar ? "" : "/en") + path;

        // ── Request bodies ─────────────────────────────────────
        public class LoginBody
        {
            public string? Login { get; set; }
            public string? Password { get; set; }
            public string? ReturnUrl { get; set; }
        }

        public class RegisterBody
        {
            public string? FullName { get; set; }
            public string? FullNameAr { get; set; }
            public string? NationalId { get; set; }
            public string? Email { get; set; }
            public string? Mobile { get; set; }
            public string? Password { get; set; }
            public string? ConfirmPassword { get; set; }
            public bool AcceptTerms { get; set; }
        }

        // ════════════════════════════════════════════════════
        // ACCOUNT
        // ════════════════════════════════════════════════════
        [HttpPost("api/account/login")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login([FromBody] LoginBody body)
        {
            var ar = Ar;
            if (string.IsNullOrWhiteSpace(body.Login) || string.IsNullOrWhiteSpace(body.Password))
                return Fail(ar ? "أدخل الرقم القومي أو البريد الإلكتروني وكلمة المرور." : "Enter your national ID or email and your password.");

            var result = await _api.LoginAsync(body.Login.Trim(), body.Password);
            if (!result.Ok || result.Data == null)
                return Fail(TranslateAuthError(result, ar));

            await ApplicantAuth.SignInAsync(HttpContext, result.Data);
            return Json(new { ok = true, redirect = SafeReturnUrl(body.ReturnUrl) ?? U("/account") });
        }

        [HttpPost("api/account/register")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register([FromBody] RegisterBody body)
        {
            var ar = Ar;
            var errors = new Dictionary<string, string[]>();

            if (body.Password != body.ConfirmPassword)
                errors["confirmPassword"] = new[] { ar ? "كلمتا المرور غير متطابقتين." : "The passwords do not match." };
            if (!body.AcceptTerms)
                errors["acceptTerms"] = new[] { ar ? "يجب الموافقة على الشروط." : "Please accept the terms." };
            if (errors.Count > 0)
                return Fail(ar ? "راجع البيانات المدخلة." : "Please check the highlighted fields.", errors);

            var result = await _api.RegisterAsync(new
            {
                fullName = body.FullName?.Trim(),
                fullNameAr = body.FullNameAr?.Trim(),
                nationalId = body.NationalId?.Trim(),
                email = body.Email?.Trim(),
                mobile = body.Mobile?.Trim(),
                password = body.Password
            });

            if (!result.Ok || result.Data == null)
                return Fail(ar ? "راجع البيانات المدخلة." : result.Message ?? "Please check the highlighted fields.", result.Errors);

            await ApplicantAuth.SignInAsync(HttpContext, result.Data);
            return Json(new { ok = true, redirect = U("/account") });
        }

        [HttpPost("account/logout")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await ApplicantAuth.SignOutAsync(HttpContext);
            return Redirect(Ar ? "/" : "/en/");
        }

        // ════════════════════════════════════════════════════
        // LOOKUPS (read-only, forwarded as-is)
        // ════════════════════════════════════════════════════
        [HttpGet("api/lookups/{name}")]
        public async Task<IActionResult> Lookup(string name)
        {
            var me = ApplicantAuth.Current(HttpContext);
            if (me == null) return SessionExpired();
            if (!AllowedLookups.Contains(name)) return NotFound();

            var result = await _api.GetLookupAsync(me.Token, name.ToLowerInvariant(), Request.QueryString.Value);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (!result.Ok) return Fail(result.Message ?? "Lookup failed.");

            return Json(new { ok = true, data = result.Data });
        }

        // ════════════════════════════════════════════════════
        // FACILITY REGISTRATION
        // ════════════════════════════════════════════════════
        [HttpPost("api/facilities")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFacility([FromBody] JsonElement body)
        {
            var me = ApplicantAuth.Current(HttpContext);
            if (me == null) return SessionExpired();
            if (body.ValueKind != JsonValueKind.Object) return BadRequest();

            var result = await _api.CreateFacilityAsync(me.Token, body.GetRawText());
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (!result.Ok)
                return Fail(Ar ? "راجع البيانات المدخلة." : result.Message ?? "Please check the form.", result.Errors);

            var id = result.Data.ValueKind == JsonValueKind.Object
                     && result.Data.TryGetProperty("id", out var idProp)
                     && idProp.TryGetInt32(out var newId) ? newId : 0;
            if (id == 0) return Json(new { ok = true, redirect = U("/account/applications") });
            return Json(new { ok = true, redirect = U("/account/facility") + "?id=" + id });
        }

        [HttpPost("api/facilities/{id:int}/documents")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<IActionResult> UploadDocument(int id, [FromForm] string documentTypeCode, IFormFile? file)
        {
            var ar = Ar;
            var me = ApplicantAuth.Current(HttpContext);
            if (me == null) return SessionExpired();
            if (file == null || file.Length == 0)
                return Fail(ar ? "اختر ملفًا أولًا." : "Choose a file first.");

            var result = await _api.UploadDocumentAsync(me.Token, id, documentTypeCode, file);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (!result.Ok)
            {
                var detail = result.Errors.Values.SelectMany(v => v).FirstOrDefault() ?? result.Message;
                return Fail(detail ?? (ar ? "تعذر رفع الملف." : "The file could not be uploaded."));
            }

            return Json(new { ok = true, data = result.Data });
        }

        [HttpPost("api/facilities/{id:int}/submit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitFacility(int id)
        {
            var me = ApplicantAuth.Current(HttpContext);
            if (me == null) return SessionExpired();

            var result = await _api.SubmitFacilityAsync(me.Token, id);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (!result.Ok) return Fail(result.Message ?? "The facility could not be submitted.");

            return Json(new
            {
                ok = true,
                message = Ar ? "تم تقديم المنشأة للاعتماد." : "Facility submitted for approval.",
                redirect = U("/account/facility") + "?id=" + id
            });
        }

        // ════════════════════════════════════════════════════
        // SERVICE CATALOGUE
        // ════════════════════════════════════════════════════

        /// <summary>
        /// The Form Builder renderer, fetched from the platform (one copy of the
        /// renderer for every channel) and cached for 10 minutes.
        /// </summary>
        [HttpGet("forms/renderer.js")]
        public async Task<IActionResult> FormRenderer()
        {
            if (!_cache.TryGetValue("portal:fb-renderer", out string? script) || script == null)
            {
                var result = await _api.GetFormRendererAsync();
                if (!result.Ok || string.IsNullOrEmpty(result.Data))
                    return StatusCode(503, "// form renderer unavailable");
                script = result.Data;
                _cache.Set("portal:fb-renderer", script, TimeSpan.FromMinutes(10));
            }
            Response.Headers.CacheControl = "private, max-age=600";
            return Content(script, "application/javascript; charset=utf-8");
        }

        public class ServiceRequestBody
        {
            public int FacilityId { get; set; }
            public JsonElement Answers { get; set; }
        }

        [HttpPost("api/services/{code}/requests")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitServiceRequest(string code, [FromBody] ServiceRequestBody body)
        {
            var ar = Ar;
            var me = ApplicantAuth.Current(HttpContext);
            if (me == null) return SessionExpired();
            if (!ServiceCode.IsMatch(code)) return NotFound();

            if (body.FacilityId <= 0)
                return Fail(ar ? "اختر المنشأة." : "Choose the facility.",
                    new() { ["facilityId"] = new[] { ar ? "اختر المنشأة." : "Choose the facility." } });

            var raw = JsonSerializer.Serialize(new { facilityId = body.FacilityId, answers = body.Answers, lang = ar ? "ar" : "en" });
            var result = await _api.SubmitServiceRequestAsync(me.Token, code, raw);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (!result.Ok)
            {
                // messages under "" are general; "facilityId" goes under the facility list
                var general = result.Errors.Where(e => e.Key == "").SelectMany(e => e.Value).ToList();
                var message = general.Count > 0 ? string.Join(" ", general)
                    : result.Message ?? (ar ? "تعذر تقديم الطلب." : "The request could not be submitted.");
                return Fail(message, result.Errors.Where(e => e.Key != "").ToDictionary(e => e.Key, e => e.Value));
            }

            var reference = result.Data.ValueKind == JsonValueKind.Object &&
                            result.Data.TryGetProperty("reference", out var refProp)
                ? refProp.GetString() : null;

            return Json(new
            {
                ok = true,
                message = ar ? $"تم تقديم طلبك برقم {reference}" : $"Your request {reference} was submitted.",
                redirect = U("/account/applications") + (reference != null ? "?submitted=" + Uri.EscapeDataString(reference) : "")
            });
        }

        public class ResubmitBody
        {
            public JsonElement Answers { get; set; }
        }

        // Batch 7: the officer returned the request — the applicant corrects the form and resubmits
        [HttpPost("api/services/requests/{reference}/resubmit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResubmitServiceRequest(string reference, [FromBody] ResubmitBody body)
        {
            var ar = Ar;
            var me = ApplicantAuth.Current(HttpContext);
            if (me == null) return SessionExpired();
            if (!RequestReference.IsMatch(reference)) return NotFound();

            var raw = JsonSerializer.Serialize(new { answers = body.Answers, lang = ar ? "ar" : "en" });
            var result = await _api.ResubmitServiceRequestAsync(me.Token, reference, raw);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (!result.Ok)
            {
                var general = result.Errors.SelectMany(e => e.Value).ToList();
                var message = general.Count > 0 ? string.Join(" ", general)
                    : result.Message ?? (ar ? "تعذر إرسال التعديل." : "Your correction could not be sent.");
                return Fail(message);
            }

            return Json(new
            {
                ok = true,
                message = ar ? $"تم إرسال التعديل على الطلب {reference}" : $"Your correction to {reference} was sent.",
                redirect = U("/account/applications") + "?submitted=" + Uri.EscapeDataString(reference)
            });
        }

        [HttpGet("api/services/{code}/options/{dataSourceId:int}")]
        public Task<IActionResult> ServiceFormOptions(string code, int dataSourceId) =>
            FormGetAsync(code, $"options/{dataSourceId}{Request.QueryString.Value}");

        [HttpGet("api/services/{code}/data/{entity}/lookup")]
        public Task<IActionResult> ServiceFormLookup(string code, string entity) =>
            EntityName.IsMatch(entity)
                ? FormGetAsync(code, $"data/{entity}/lookup{Request.QueryString.Value}")
                : Task.FromResult<IActionResult>(Json(new { success = false }));

        [HttpPost("api/services/{code}/rules/evaluate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ServiceFormRule(string code, [FromBody] JsonElement body)
        {
            var me = ApplicantAuth.Current(HttpContext);
            if (me == null || !ServiceCode.IsMatch(code)) return Json(new { success = false, message = "Not available." });
            var result = await _api.ServiceFormRuleAsync(me.Token, code, body.GetRawText());
            return FormAnswer(result);
        }

        [HttpPost("api/services/{code}/upload")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(60 * 1024 * 1024)]
        public async Task<IActionResult> ServiceFormUpload(string code, [FromForm] string fieldKey, [FromForm] List<IFormFile> files)
        {
            var me = ApplicantAuth.Current(HttpContext);
            if (me == null || !ServiceCode.IsMatch(code)) return Json(new { success = false, message = "Not available." });
            if (files == null || files.Count == 0) return Json(new { success = false, message = Ar ? "اختر ملفًا." : "Choose a file." });
            var result = await _api.ServiceFormUploadAsync(me.Token, code, fieldKey, files);
            return FormAnswer(result);
        }

        private async Task<IActionResult> FormGetAsync(string code, string relativePath)
        {
            var me = ApplicantAuth.Current(HttpContext);
            if (me == null || !ServiceCode.IsMatch(code)) return Json(new { success = false, message = "Not available." });
            var result = await _api.ServiceFormGetAsync(me.Token, code, relativePath);
            return FormAnswer(result);
        }

        /// <summary>The platform's answer, as-is (the renderer reads { success, ... }).</summary>
        private JsonResult FormAnswer(ApiResult<JsonElement> result) =>
            result.Ok && result.Data.ValueKind == JsonValueKind.Object
                ? Json(result.Data)
                : Json(new { success = false, message = result.Message ?? "Not available." });

        // ════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════
        private JsonResult Fail(string message, Dictionary<string, string[]>? errors = null) =>
            Json(new { ok = false, message, errors });

        private JsonResult SessionExpired() =>
            Json(new
            {
                ok = false,
                message = Ar ? "انتهت الجلسة. سجّل الدخول مرة أخرى." : "Your session has ended. Please sign in again.",
                redirect = U("/account/login")
            });

        private async Task<JsonResult> SessionExpiredAsync()
        {
            await ApplicantAuth.SignOutAsync(HttpContext);
            return SessionExpired();
        }

        private static string TranslateAuthError(ApiResult<AuthResponse> r, bool ar) => r.Status switch
        {
            401 => ar ? "بيانات الدخول غير صحيحة." : "Invalid login or password.",
            423 => ar ? "تم إيقاف الحساب مؤقتًا بسبب محاولات خاطئة. حاول بعد 5 دقائق." : "Account temporarily locked. Please try again in 5 minutes.",
            429 => ar ? "محاولات كثيرة. انتظر دقيقة ثم حاول مرة أخرى." : "Too many attempts. Please wait a minute and try again.",
            503 => ar ? "الخدمة غير متاحة حاليًا. حاول لاحقًا." : "The service is temporarily unavailable. Please try again later.",
            _ => r.Message ?? (ar ? "تعذر تسجيل الدخول." : "Sign-in failed.")
        };

        /// <summary>Only local paths are allowed as return addresses (no open redirect).</summary>
        private string? SafeReturnUrl(string? url) =>
            !string.IsNullOrWhiteSpace(url) && Url.IsLocalUrl(url) && !url.StartsWith("/portal", StringComparison.OrdinalIgnoreCase)
                ? url
                : null;
    }
}
