// ============================================================
// FILE: Services/Applicant/ApplicantUi.cs
// Small display helpers shared by the applicant screens:
// facility status labels (Arabic / English) and the pill style.
// ============================================================
using System.Globalization;

namespace Egabi.Portal.Services.Applicant
{
    public static class ApplicantUi
    {
        public static bool IsArabic =>
            CultureInfo.CurrentCulture.Name.StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        /// <summary>Adds "/en" in front of a path on English pages.</summary>
        public static string Url(string path) => (IsArabic ? "" : "/en") + path;

        public static bool IsDraft(string status) => status is "Draft";
        public static bool IsReturned(string status) => status is "Rejected";
        public static bool IsApproved(string status) => status is "FinalApproved";
        public static bool IsInReview(string status) => !IsDraft(status) && !IsReturned(status) && !IsApproved(status);

        /// <summary>True when the applicant has something to do (upload / submit).</summary>
        public static bool NeedsAction(string status) => IsDraft(status) || IsReturned(status);

        public static string StatusLabel(string status, bool ar) => status switch
        {
            "Draft" => ar ? "مسودة" : "Draft",
            "Rejected" => ar ? "مُعادة للاستكمال" : "Returned",
            "FinalApproved" => ar ? "معتمدة" : "Approved",
            _ => ar ? "قيد المراجعة" : "Under review"
        };

        public static string StatusPill(string status) => status switch
        {
            "Draft" => "gp-pill gp-pill--muted",
            "Rejected" => "gp-pill gp-pill--warn",
            "FinalApproved" => "gp-pill gp-pill--ok",
            _ => "gp-pill gp-pill--info"
        };

        public static string Reference(int facilityId) => $"FAC-{facilityId:000000}";

        public static string Date(DateTime? d, bool ar) =>
            d == null ? "—" : (d.Value.Kind == DateTimeKind.Utc ? d.Value.ToLocalTime() : d.Value).ToString("dd MMM yyyy", ar ? new CultureInfo("ar-EG") : new CultureInfo("en-GB"));

        // ── Service requests (Service Catalogue) ─────────────
        public static string RequestStatusLabel(string status, bool ar) => status switch
        {
            "Completed" => ar ? "مكتمل" : "Completed",
            "ActionRequired" => ar ? "مطلوب إجراء منك" : "Action required",
            "Rejected" => ar ? "مرفوض" : "Rejected",
            "Cancelled" => ar ? "ملغى" : "Cancelled",
            "WorkflowError" or "Submitted" => ar ? "تم الاستلام" : "Received",
            _ => ar ? "قيد المعالجة" : "In progress"
        };

        public static string RequestStatusPill(string status) => status switch
        {
            "Completed" => "gp-pill gp-pill--ok",
            "ActionRequired" => "gp-pill gp-pill--warn",
            "Rejected" => "gp-pill gp-pill--bad",
            "Cancelled" => "gp-pill gp-pill--muted",
            _ => "gp-pill gp-pill--info"

        };

        public static string VerificationLabel(string status, bool ar) => status switch
        {
            "Verified" => ar ? "تم التحقق" : "Verified",
            "Pending" => ar ? "قيد التحقق" : "Being checked",
            "Mismatch" => ar ? "بيانات غير مطابقة" : "Data mismatch",
            "Unreadable" => ar ? "غير مقروء" : "Unreadable",
            _ => status
        };

        public static string VerificationPill(string status) => status switch
        {
            "Verified" => "gp-pill gp-pill--ok",
            "Mismatch" or "Unreadable" => "gp-pill gp-pill--warn",
            _ => "gp-pill"
        };
    }
}
