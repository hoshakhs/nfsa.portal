// ============================================================
// FILE: Services/InspectPro/InspectProModels.cs
// Data shapes returned by the egabi Platform (InspectPro)
// Public API v1. Property names match the API's JSON (camelCase).
// ============================================================
namespace Egabi.Portal.Services.InspectPro
{
    public class InspectProApiOptions
    {
        /// <summary>Address of InspectPro, e.g. http://localhost:5103/</summary>
        public string BaseUrl { get; set; } = "http://localhost:5103/";

        /// <summary>Same value as PublicApi:PortalClientKey in InspectPro's appsettings.json.</summary>
        public string PortalClientKey { get; set; } = string.Empty;

        public int TimeoutSeconds { get; set; } = 60;
    }

    /// <summary>Result of one API call: data on success, messages on failure.</summary>
    public class ApiResult<T>
    {
        public bool Ok { get; init; }
        public int Status { get; init; }
        public T? Data { get; init; }
        public string? Message { get; init; }
        public Dictionary<string, string[]> Errors { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public string? CorrelationId { get; init; }

        /// <summary>The applicant's token was rejected (expired or invalid).</summary>
        public bool Unauthorized => Status == 401;
    }

    public record ApplicantDto(string Id, string? FullName, string? FullNameAr, string? Email, string? Mobile, string? NationalIdMasked);

    public record AuthResponse(string Token, DateTime ExpiresAtUtc, ApplicantDto Applicant);

    public record FacilitySummary(
        int Id, string? FacilityName, string? FacilityNameLocal,
        string? SectorNameEn, string? SectorNameAr,
        string ApprovalStatus, int CurrentApprovalLevel, DateTime? SubmittedAt);

    public record StageDto(int Level, string LabelEn, string State, DateTime? Date, string? ReturnReason);

    public record DocumentDto(int Id, string? Code, string? Label, string? FileName, string VerificationStatus, DateTime UploadedDate);

    public record RequiredDocumentDto(string Code, string NameEn, string NameAr, bool IsRequired, bool IsUploaded);

    public record OwnerDto(string? OwnerName, string? OwnerNameAr, string? OwnerNationality, string? OwnerIDNumber,
                           string? OwnerIDType, decimal? OwnershipPercentage, string? Role, string? Email, string? Phone);

    public record ContactDto(string? ContactName, string? Position, string? PhoneNumber, string? Mobile, string? Email, bool IsPrimary);

    public record FacilityDetails(
        int Id, string? FacilityName, string? FacilityNameLocal, string? TradeName,
        int? SectorID, string? SectorNameEn, string? SectorNameAr,
        int? FacilityTypeID, string? FacilityTypeNameEn, string? FacilityTypeNameAr,
        string? Governorate, string? AdministrativeArea, string? District, string? Street,
        string ApprovalStatus, int CurrentApprovalLevel, int TotalApprovalLevels,
        bool CanUploadDocuments, bool CanSubmit, DateTime? SubmittedAt,
        List<StageDto> Stages,
        List<DocumentDto> Documents,
        List<RequiredDocumentDto> RequiredDocuments,
        List<OwnerDto> Owners,
        List<ContactDto> Contacts,
        List<CustomFieldValue>? CustomFields,
        // NFSA N4: why the application is back with me, and the establishment's licence
        string? ReturnReason = null, DateTime? ReturnedAt = null, bool IsReturned = false,
        string? LicenceNumber = null, DateTime? LicenceValidUntil = null, string? LicenceStatus = null);

    /// <summary>A custom field added in the platform's Form Config, with the saved value (as text).</summary>
    public record CustomFieldValue(string FieldName, string Label, string? LabelAr, string FieldType, string? Value);

    // ── Service Catalogue ───────────────────────────────────
    public record ServiceSummary(
        string Code, string NameEn, string NameAr,
        string? CategoryEn, string? CategoryAr,
        string? DescriptionEn, string? DescriptionAr,
        string? Icon, decimal? FeeAmount, int? ProcessingDays, bool RequiresApprovedFacility,
        ServiceInfo? Info);

    /// <summary>Service details from the platform's Service Catalogue (one item per line).</summary>
    public record ServiceInfo(
        string? ConditionsEn, string? ConditionsAr,
        string? RequiredDocumentsEn, string? RequiredDocumentsAr,
        string? StepsEn, string? StepsAr,
        string? OutputEn, string? OutputAr)
    {
        public static List<string> Lines(string? text) =>
            (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public record ServiceDetails(
        string Code, string NameEn, string NameAr,
        string? CategoryEn, string? CategoryAr,
        string? DescriptionEn, string? DescriptionAr,
        string? Icon, decimal? FeeAmount, int? ProcessingDays, bool RequiresApprovedFacility,
        int FormVersion, System.Text.Json.JsonElement Schema, ServiceInfo? Info)
    {
        public ServiceSummary Summary => new(Code, NameEn, NameAr, CategoryEn, CategoryAr, DescriptionEn, DescriptionAr,
                                             Icon, FeeAmount, ProcessingDays, RequiresApprovedFacility, Info);
    }

    public record ServiceRequestItem(
        int Id, string Reference,
        string ServiceCode, string ServiceNameEn, string ServiceNameAr,
        int FacilityId, string? FacilityName, string? FacilityNameLocal,
        string Status, string? CurrentStage, DateTime SubmittedAt);

    /// <summary>
    /// Batch 7: one of my service requests with what I submitted — used by
    /// "Correct and resubmit" when an officer returned the request.
    /// </summary>
    public record ServiceRequestDetails(
        int Id, string Reference,
        string ServiceCode, string ServiceNameEn, string ServiceNameAr,
        int FacilityId, string? FacilityName, string? FacilityNameLocal,
        string Status, string? CurrentStage, DateTime SubmittedAt,
        string? ReturnNote, DateTime? ReturnedAt, int ResubmissionCount,
        int FormVersion, System.Text.Json.JsonElement Schema, System.Text.Json.JsonElement Answers);

    // ── Certificates (NFSA N4) ──────────────────────────────

    /// <summary>One of my certificates (licence, registration…). Status: Valid | Expired | Revoked.</summary>
    public record CertificateItem(
        string CertificateNumber, string CertificateType, string TitleEn, string TitleAr,
        string? HolderNameEn, string? HolderNameAr,
        DateTime IssuedAt, DateTime? ValidUntil, string Status, string VerificationCode);

    public record CertificateDetail(string LabelEn, string LabelAr, string? ValueEn, string? ValueAr);

    /// <summary>Public verification of a certificate by its code (the QR code opens it).</summary>
    public record CertificateVerification(
        string CertificateNumber, string CertificateType, string TitleEn, string TitleAr,
        string? HolderNameEn, string? HolderNameAr,
        DateTime IssuedAt, DateTime? ValidUntil,
        string Status, DateTime? RevokedAt,
        string IssuerNameEn, string IssuerNameAr,
        List<CertificateDetail> Details);
}

