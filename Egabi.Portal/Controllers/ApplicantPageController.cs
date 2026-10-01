// ============================================================
// FILE: Controllers/ApplicantPageController.cs
// Runs before every "Applicant Page" (Umbraco route hijacking —
// the class name matches the document type alias "applicantPage").
//   • Signed-out visitor on a protected screen → sign-in page
//     (and back to the same page after signing in)
//   • Signed-in applicant on sign-in / create-account → dashboard
//   • Public screens (verify a certificate — NFSA N4) → anyone
// ============================================================
using Egabi.Portal.Services.Applicant;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Extensions;

namespace Egabi.Portal.Controllers
{
    public class ApplicantPageController : RenderController
    {
        private static readonly string[] GuestScreens = { "login", "register" };
        private static readonly string[] PublicScreens = { "verify" };

        public ApplicantPageController(
            ILogger<ApplicantPageController> logger,
            ICompositeViewEngine compositeViewEngine,
            IUmbracoContextAccessor umbracoContextAccessor)
            : base(logger, compositeViewEngine, umbracoContextAccessor)
        {
        }

        public override IActionResult Index()
        {
            var page = CurrentPage;
            if (page == null) return NotFound();

            var screen = page.Value<string>("screen") ?? "dashboard";
            if (PublicScreens.Contains(screen))
                return CurrentTemplate(page);

            var isGuestScreen = GuestScreens.Contains(screen);
            var applicant = ApplicantAuth.Current(HttpContext);

            if (!isGuestScreen && applicant == null)
            {
                var here = Request.Path + Request.QueryString;
                return Redirect(ApplicantUi.Url("/account/login") + "?returnUrl=" + Uri.EscapeDataString(here));
            }

            if (isGuestScreen && applicant != null)
                return Redirect(ApplicantUi.Url("/account"));

            return CurrentTemplate(page);
        }
    }
}
