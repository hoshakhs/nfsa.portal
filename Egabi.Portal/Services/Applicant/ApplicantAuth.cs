// ============================================================
// FILE: Services/Applicant/ApplicantAuth.cs
// Applicant sign-in for the portal.
//
// After a successful login/registration against the egabi Platform
// API, the applicant's API token is kept inside an ENCRYPTED,
// HttpOnly cookie (".EgabiPortal.Applicant"). The cookie expires
// together with the token. This is separate from Umbraco's own
// back-office users and members.
// ============================================================
using System.Security.Claims;
using Egabi.Portal.Services.InspectPro;
using Microsoft.AspNetCore.Authentication;

namespace Egabi.Portal.Services.Applicant
{
    /// <summary>The signed-in applicant for the current request.</summary>
    public record ApplicantSession(string Id, string DisplayName, string? DisplayNameAr, string? Email, string Token)
    {
        public string Initials
        {
            get
            {
                var parts = (DisplayName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var s = string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
                return string.IsNullOrEmpty(s) ? "?" : s;
            }
        }

        public string NameFor(bool isArabic) =>
            isArabic && !string.IsNullOrWhiteSpace(DisplayNameAr) ? DisplayNameAr! : DisplayName;
    }

    public static class ApplicantAuth
    {
        public const string Scheme = "EgabiApplicant";
        private const string ItemKey = "Egabi.ApplicantSession";
        private const string TokenName = "access_token";

        /// <summary>Signs the applicant in (called after API login / registration).</summary>
        public static async Task SignInAsync(HttpContext ctx, AuthResponse auth)
        {
            var a = auth.Applicant;
            var name = string.IsNullOrWhiteSpace(a.FullName) ? (a.Email ?? "Applicant") : a.FullName!;

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, a.Id),
                new Claim(ClaimTypes.Name, name),
                new Claim("name_ar", a.FullNameAr ?? string.Empty),
                new Claim(ClaimTypes.Email, a.Email ?? string.Empty)
            }, Scheme);

            var props = new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTime.SpecifyKind(auth.ExpiresAtUtc, DateTimeKind.Utc)
            };
            props.StoreTokens(new[] { new AuthenticationToken { Name = TokenName, Value = auth.Token } });

            await ctx.SignInAsync(Scheme, new ClaimsPrincipal(identity), props);
        }

        public static Task SignOutAsync(HttpContext ctx) => ctx.SignOutAsync(Scheme);

        /// <summary>
        /// Reads the applicant cookie once per request (called by a small middleware
        /// in Program.cs) so pages and controllers can use Current() synchronously.
        /// </summary>
        public static async Task LoadAsync(HttpContext ctx)
        {
            var result = await ctx.AuthenticateAsync(Scheme);
            if (!result.Succeeded || result.Principal == null) return;

            var token = result.Properties?.GetTokenValue(TokenName);
            if (string.IsNullOrEmpty(token)) return;

            var p = result.Principal;
            var nameAr = p.FindFirstValue("name_ar");

            ctx.Items[ItemKey] = new ApplicantSession(
                p.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                p.FindFirstValue(ClaimTypes.Name) ?? "Applicant",
                string.IsNullOrWhiteSpace(nameAr) ? null : nameAr,
                p.FindFirstValue(ClaimTypes.Email),
                token);
        }

        /// <summary>The signed-in applicant, or null.</summary>
        public static ApplicantSession? Current(HttpContext? ctx) =>
            ctx?.Items[ItemKey] as ApplicantSession;
    }
}
