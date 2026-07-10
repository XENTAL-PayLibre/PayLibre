using System.ComponentModel.DataAnnotations;

namespace PayLibre.Api.Contracts;

// ---- Auth & school ----
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

public sealed record SchoolResponse(
    Guid Id, string Name, string OfficialEmail, string Phone, string Status,
    string SettlementBankName, string SettlementAccountNumber, string? SettlementAccountName);

public sealed record MeResponse(Guid UserId, string Email, string Role, SchoolResponse School);

// ---- Classes ----
public sealed record ClassRequest(
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [Required, StringLength(40, MinimumLength = 1)] string Session);

public sealed record ClassResponse(Guid Id, string Name, string Session);

// ---- Students ----
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
