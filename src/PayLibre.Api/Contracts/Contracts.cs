using System.ComponentModel.DataAnnotations;

namespace PayLibre.Api.Contracts;

// ---- Auth & school ----
/// <summary>Register a school + its owner. <c>SettlementBankName</c>/<c>SettlementBankCode</c> come from
/// <c>GET /api/v1/banks</c>; the account name is resolved by the provider. Sets the session cookies.</summary>
public sealed record RegisterSchoolRequest(
    [Required, StringLength(200, MinimumLength = 2)] string SchoolName,
    [Required, EmailAddress] string OfficialEmail,
    [Required, StringLength(40, MinimumLength = 5)] string Phone,
    [Required, StringLength(200)] string SettlementBankName,
    [Required, StringLength(16)] string SettlementBankCode,
    [Required, StringLength(20, MinimumLength = 6)] string SettlementAccountNumber,
    [Required, StringLength(200, MinimumLength = 8)] string Password);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

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
    string SettlementBankName, string SettlementAccountNumber, string? SettlementAccountName, string? JoinCode);

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
    [Required] DateTimeOffset DueDateUtc);

/// <summary>A fee with its rolled-up collection figures (all money in kobo).</summary>
public sealed record FeeResponse(
    Guid Id, string Name, Guid FeeCategoryId, string CategoryName, Guid ClassId, string ClassName,
    string Session, string Term, long AmountKobo, DateTimeOffset DueDateUtc,
    int Students, long InvoicedKobo, long CollectedKobo, long OutstandingKobo);

public sealed record FeeSummaryResponse(long TotalInvoicedKobo, long CollectedKobo, long OutstandingKobo, int Fees, int Invoices);

/// <summary>A single student's invoice for a fee.</summary>
public sealed record StudentFeeResponse(
    Guid Id, Guid StudentId, string StudentName, string AdmissionNo, string ClassName,
    long AmountKobo, long AmountPaidKobo, long OutstandingKobo, string Status, DateTimeOffset DueDateUtc);

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

// ---- Payments ----
/// <summary>A reconciled payment received into a student's virtual account (all money in kobo).</summary>
public sealed record PaymentResponse(
    Guid Id, Guid StudentId, string StudentName, string AdmissionNo,
    long AmountKobo, long NetCreditKobo, string? PayerName, DateTimeOffset OccurredAtUtc);
