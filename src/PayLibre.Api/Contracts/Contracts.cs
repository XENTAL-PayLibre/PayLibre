using System.ComponentModel.DataAnnotations;

namespace PayLibre.Api.Contracts;

// ---- Auth & school ----
/// <summary>Register a school + its owner. Settlement (payout) bank details are NOT collected here — the
/// school configures where fees settle later from Settings (<c>PUT /api/v1/schools/settlement</c>).
/// Sets the session cookies and returns an access token.</summary>
public sealed record RegisterSchoolRequest(
    [Required, StringLength(200, MinimumLength = 2)] string SchoolName,
    [Required, EmailAddress] string OfficialEmail,
    [Required, StringLength(40, MinimumLength = 5)] string Phone,
    [Required, StringLength(200, MinimumLength = 8)] string Password);

/// <summary>Set (or change) the school's payout account. <c>BankName</c>/<c>BankCode</c> come from
/// <c>GET /api/v1/banks</c>; the account holder name is resolved by the provider.</summary>
public sealed record UpdateSettlementRequest(
    [Required, StringLength(200)] string BankName,
    [Required, StringLength(16)] string BankCode,
    [Required, StringLength(20, MinimumLength = 6)] string AccountNumber);

/// <summary>Set the school's late-fee policy. <c>LateFeeBps</c> is a percentage of the outstanding balance
/// in basis points (500 = 5%); 0 disables late fees. <c>GraceDays</c> is days after due before it applies.</summary>
public sealed record UpdateLateFeesRequest(
    [Range(0, 10_000)] int LateFeeBps,
    [Range(0, 365)] int GraceDays);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

/// <summary>Step 2 of login: the emailed one-time code.</summary>
public sealed record VerifyOtpRequest([Required, EmailAddress] string Email, [Required] string Code);

/// <summary>Step 1 result: a sign-in code was emailed; call the verify endpoint to finish.</summary>
public sealed record LoginChallengeResponse(string Email, DateTimeOffset ExpiresAtUtc, string Message);

/// <summary>Request a password-reset link (always 202, whether or not the email exists).</summary>
public sealed record ForgotPasswordRequest([Required, EmailAddress] string Email);

/// <summary>Set a new password using the token from the reset email.</summary>
public sealed record ResetPasswordRequest(
    [Required] string Token,
    [Required, StringLength(200, MinimumLength = 8)] string NewPassword);

/// <summary>Auth result: sets HttpOnly session cookies AND returns a bearer access token for
/// mobile/API clients. Send it as <c>Authorization: Bearer &lt;accessToken&gt;</c>. The refresh token
/// is delivered only as an HttpOnly cookie; call <c>/auth/refresh</c> to get a new access token.</summary>
public sealed record AuthSessionResponse(
    SchoolResponse School, Guid UserId, string Email, string Role,
    string AccessToken, string TokenType, int ExpiresIn);

public sealed record SchoolResponse(
    Guid Id, string Name, string OfficialEmail, string Phone, string Status,
    string? SettlementBankName, string? SettlementAccountNumber, string? SettlementAccountName,
    bool SettlementConfigured, int LateFeeBps, int LateFeeGraceDays, string? JoinCode);

// ---- Parent self-enrolment (public, code-based) ----
public sealed record EnrolContextClassResponse(Guid Id, string Name, string Session);
public sealed record EnrolContextResponse(string SchoolName, IReadOnlyList<EnrolContextClassResponse> Classes);
/// <summary>A parent self-enrolling their child via the school's join code (no login).</summary>
public sealed record SelfEnrolRequest(
    [Required, StringLength(200, MinimumLength = 1)] string FullName,
    [Required] Guid ClassId,
    [Required, StringLength(200, MinimumLength = 1)] string GuardianName,
    [StringLength(40)] string? GuardianPhone,
    [EmailAddress] string? GuardianEmail);

public sealed record MeResponse(Guid UserId, string Email, string Role, SchoolResponse School);

// ---- API keys ----
/// <summary>Create a scoped API key. Scopes: students:read, students:write, payments:read.</summary>
public sealed record CreateApiKeyRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [Required, MinLength(1)] string[] Scopes);
public sealed record ApiKeyResponse(
    Guid Id, string Name, string KeyPrefix, string Scopes, bool Active,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? LastUsedAtUtc, DateTimeOffset? RevokedAtUtc);
/// <summary>Returned only once, at creation — the full key value is never shown again.</summary>
public sealed record CreatedApiKeyResponse(ApiKeyResponse Key, string PlaintextKey);

// ---- Outbound webhook subscriptions ----
/// <summary>Register a school endpoint to receive signed PayLibre events (e.g. payment.received).</summary>
public sealed record CreateWebhookSubscriptionRequest([Required, Url, StringLength(500)] string Url);
public sealed record WebhookSubscriptionResponse(
    Guid Id, string Url, bool Active, DateTimeOffset CreatedAtUtc, DateTimeOffset? RevokedAtUtc);
/// <summary>Returned only once, at creation — the signing secret is never shown again.</summary>
public sealed record CreatedWebhookSubscriptionResponse(WebhookSubscriptionResponse Subscription, string SigningSecret);

// ---- Public API (X-Api-Key) ----
/// <summary>Create or update a student by admission number (idempotent sync from a school system).</summary>
public sealed record PublicUpsertStudentRequest(
    [Required, StringLength(64, MinimumLength = 1)] string AdmissionNo,
    [Required, StringLength(200, MinimumLength = 1)] string FullName,
    [Required] Guid ClassId,
    [StringLength(40)] string? Session,
    [Required, StringLength(200, MinimumLength = 1)] string GuardianName,
    [StringLength(40)] string? GuardianPhone,
    [EmailAddress] string? GuardianEmail);
public sealed record PublicStudentResponse(
    string AdmissionNo, string FullName, string Status, long OutstandingKobo,
    string? Nuban, string? BankName, string? AccountName);

// ---- Team / invites ----
/// <summary>Invite a staff member. <c>Role</c> is Admin, Bursar, Accountant, Auditor, or ClassTeacher (not
/// Owner). <c>ClassIds</c> is required for ClassTeacher (the classes they may access) and ignored otherwise.</summary>
public sealed record CreateInviteRequest(
    [Required, EmailAddress] string Email,
    [Required, RegularExpression("^(Admin|Bursar|Accountant|Auditor|ClassTeacher)$", ErrorMessage = "Role must be Admin, Bursar, Accountant, Auditor, or ClassTeacher.")] string Role,
    Guid[]? ClassIds);
/// <summary>Replace a class teacher's assigned classes (takes effect on their next sign-in).</summary>
public sealed record SetUserClassesRequest([Required] Guid[] ClassIds);
/// <summary>Accept a staff invitation (from the emailed link) and set a password. Then sign in normally.</summary>
public sealed record AcceptInviteRequest([Required] string Token, [Required, StringLength(200, MinimumLength = 8)] string Password);
public sealed record InviteResponse(
    Guid Id, string Email, string Role, DateTimeOffset ExpiresAtUtc, DateTimeOffset? AcceptedAtUtc, string? InvitedByEmail);

// ---- Audit trail ----
/// <summary>One entry in the school's audit trail (who did what, when).</summary>
public sealed record AuditEventResponse(
    Guid Id, DateTimeOffset OccurredAtUtc, string? ActorEmail, string Action,
    string? EntityType, Guid? EntityId, string Summary);

// ---- Classes ----
public sealed record ClassRequest(
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [Required, StringLength(40, MinimumLength = 1)] string Session);

public sealed record ClassResponse(Guid Id, string Name, string Session);

// ---- Students ----
/// <summary>Create/update a student. <c>ClassId</c> is from <c>GET /api/v1/classes</c>; <c>Session</c> is
/// optional and defaults to the class's session. Creating a student auto-provisions its virtual account.</summary>
public sealed record CreateStudentRequest(
    [Required, StringLength(64, MinimumLength = 1)] string AdmissionNo,
    [Required, StringLength(200, MinimumLength = 1)] string FullName,
    [Required] Guid ClassId,
    [StringLength(40)] string? Session,
    [Required, StringLength(200, MinimumLength = 1)] string GuardianName,
    [StringLength(40)] string? GuardianPhone,
    [EmailAddress] string? GuardianEmail);

public sealed record StudentResponse(
    Guid Id, string AdmissionNo, string FullName, Guid ClassId, string Session,
    string GuardianName, string? GuardianPhone, string? GuardianEmail, string Status,
    bool HasVirtualAccount, string? Nuban, string? BankName, string? AccountName);

public sealed record VirtualAccountResponse(string Nuban, string BankName, string AccountName);

/// <summary>Add an additional guardian to a student (multi-guardian) — they can then view + pay.</summary>
public sealed record AddGuardianRequest(
    [Required, EmailAddress] string Email, [StringLength(200)] string? Name, [StringLength(40)] string? Phone);
public sealed record GuardianResponse(Guid Id, Guid StudentId, string Email, string? Name, string? Phone);

/// <summary>Term rollover: move the selected students to a target class (+ optional session override).</summary>
public sealed record PromoteStudentsRequest(
    [Required, MinLength(1)] Guid[] StudentIds,
    [Required] Guid ToClassId,
    [StringLength(40)] string? Session);
/// <summary>Bulk activate/deactivate students.</summary>
public sealed record BulkStatusRequest(
    [Required, MinLength(1)] Guid[] StudentIds,
    [Required, RegularExpression("^(Active|Inactive)$", ErrorMessage = "Status must be Active or Inactive.")] string Status);
public sealed record BulkResultResponse(int Affected);

public sealed record ImportResultResponse(int Created, int Failed, IReadOnlyList<ImportErrorResponse> Errors);
public sealed record ImportErrorResponse(int Row, string Message);

public sealed record BankResponse(string Name, string Code);

// ---- Fee categories ----
public sealed record FeeCategoryRequest([Required, StringLength(120, MinimumLength = 1)] string Name);
public sealed record FeeCategoryResponse(Guid Id, string Name);

// ---- Fees ----
/// <summary>Create a fee for a class/term; fans out an invoice to every active student in the class.
/// <c>Amount</c> is in kobo (₦1 = 100 kobo). <c>Session</c> defaults to the class's session.</summary>
public sealed record CreateFeeRequest(
    [Required, StringLength(160, MinimumLength = 1)] string Name,
    [Required] Guid FeeCategoryId,
    [Required] Guid ClassId,
    [StringLength(40)] string? Session,
    [Required, RegularExpression("^(First|Second|Third)$", ErrorMessage = "Term must be First, Second, or Third.")] string Term,
    [Range(1, long.MaxValue)] long AmountKobo,
    [Required] DateTimeOffset DueDateUtc,
    bool AppliesLateFee = true);

/// <summary>A fee with its rolled-up collection figures (all money in kobo).</summary>
public sealed record FeeResponse(
    Guid Id, string Name, Guid FeeCategoryId, string CategoryName, Guid ClassId, string ClassName,
    string Session, string Term, long AmountKobo, DateTimeOffset DueDateUtc,
    int Students, long InvoicedKobo, long CollectedKobo, long OutstandingKobo, bool AppliesLateFee);

public sealed record FeeSummaryResponse(long TotalInvoicedKobo, long CollectedKobo, long OutstandingKobo, int Fees, int Invoices);

/// <summary>A single student's invoice for a fee.</summary>
public sealed record StudentFeeResponse(
    Guid Id, Guid StudentId, string StudentName, string AdmissionNo, string ClassName,
    long AmountKobo, long AmountPaidKobo, long OutstandingKobo, string Status, DateTimeOffset DueDateUtc,
    long LateFeeAppliedKobo);

public sealed record FeeDetailResponse(FeeResponse Fee, IReadOnlyList<StudentFeeResponse> Invoices);

// ---- Parent app ----
public sealed record ParentRegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, StringLength(200, MinimumLength = 8)] string Password,
    [StringLength(200)] string? FullName,
    [StringLength(40)] string? Phone);
public sealed record ParentLoginRequest([Required, EmailAddress] string Email, [Required] string Password);
/// <summary>Parent auth result — a bearer token for the mobile app (send as Authorization: Bearer).</summary>
public sealed record ParentSessionResponse(Guid ParentId, string Email, string AccessToken, string TokenType, int ExpiresIn);

public sealed record ParentChildResponse(
    Guid StudentId, string FullName, string AdmissionNo, string SchoolName, string ClassName,
    string? Nuban, string? BankName, string? AccountName, long OutstandingKobo);
public sealed record ParentFeeResponse(
    Guid StudentFeeId, string FeeName, long AmountKobo, long AmountPaidKobo, long OutstandingKobo, string Status, DateTimeOffset DueDateUtc);
public sealed record ParentPaymentDetailsResponse(string StudentName, string Nuban, string BankName, string AccountName, long OutstandingKobo);
public sealed record ParentPaymentResponse(Guid Id, string StudentName, long AmountKobo, DateTimeOffset OccurredAtUtc);

// ---- Disputes ----
/// <summary>Parent raises a dispute about a payment (mis-attributed / missing / wrong amount).</summary>
public sealed record RaiseDisputeRequest([Required, StringLength(1000, MinimumLength = 3)] string Reason);
/// <summary>Staff resolve a dispute: accept (valid) or reject, with an optional note.</summary>
public sealed record ResolveDisputeRequest([Required] bool Accepted, [StringLength(1000)] string? Resolution);
public sealed record DisputeResponse(
    Guid Id, Guid PaymentId, string Status, string RaisedByEmail, string Reason,
    string? Resolution, string? ResolvedByEmail, DateTimeOffset RaisedAtUtc, DateTimeOffset? ResolvedAtUtc);

// ---- Settlement report ----
/// <summary>The school's settlement position at the provider (net kobo) + its payout account.</summary>
public sealed record SettlementReportResponse(
    bool Configured, long CollectedKobo, long SettledKobo, long PendingKobo, int VirtualAccounts,
    string? BankName, string? AccountNumber, string? AccountName);

// ---- Refunds (dual-control) ----
/// <summary>Request a refund of a received payment. A different Owner/Admin must approve it to execute.</summary>
public sealed record CreateRefundRequest([Required] Guid PaymentId, [StringLength(500)] string? Reason);
public sealed record RejectRefundRequest([StringLength(500)] string? Note);
public sealed record RefundRequestResponse(
    Guid Id, Guid PaymentId, string Status, string? RequestedByEmail, string? DecidedByEmail,
    string? Reason, string? DecisionNote, long AmountKobo, string? FailureReason,
    DateTimeOffset RequestedAtUtc, DateTimeOffset? DecidedAtUtc);

// ---- Payments ----
/// <summary>A reconciled payment received into a student's virtual account (all money in kobo).</summary>
public sealed record PaymentResponse(
    Guid Id, Guid StudentId, string StudentName, string AdmissionNo,
    long AmountKobo, long NetCreditKobo, string? PayerName, DateTimeOffset OccurredAtUtc);
