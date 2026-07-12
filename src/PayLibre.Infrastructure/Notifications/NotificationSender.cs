using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Infrastructure.Notifications;

/// <summary>
/// Sends account details (email + SMS) and password-reset emails. Email via Resend, SMS via Termii
/// (or Twilio). Each channel is used only when configured; otherwise the message is logged, so the
/// app works out of the box without provider credentials.
/// </summary>
public sealed class NotificationSender(
    IHttpClientFactory httpFactory,
    IOptions<ResendOptions> resend,
    IOptions<SmsOptions> sms,
    ILogger<NotificationSender> logger) : INotificationSender
{
    private readonly ResendOptions _resend = resend.Value;
    private readonly SmsOptions _sms = sms.Value;

    public async Task SendVirtualAccountDetailsAsync(
        string toName, string? email, string? phone, string studentName,
        string nuban, string bankName, string accountName, CancellationToken ct = default)
    {
        var text = $"{studentName}'s PayLibre fee account: {bankName}, {nuban} ({accountName}). "
            + "Transfer school fees to this account — payments are tracked automatically.";
        var subject = $"Fee account for {studentName}";
        var html = $"<p>Dear {toName},</p><p>{text}</p>";

        logger.LogInformation("Account details for {Student} -> {Email}/{Phone}: {Bank} {Nuban}",
            studentName, email ?? "-", phone ?? "-", bankName, nuban);

        if (!string.IsNullOrWhiteSpace(email)) await SendEmailAsync(email!, subject, html, ct);
        if (!string.IsNullOrWhiteSpace(phone)) await SendSmsAsync(phone!, text, ct);
    }

    public async Task SendWelcomeAsync(string toEmail, string name, CancellationToken ct = default)
    {
        var html = $"<p>Hi {name},</p><p>Welcome to PayLibre — your account is ready. "
            + "You can now set up classes, students and fees, and start collecting school payments.</p>";
        logger.LogInformation("Welcome email for {Email}", toEmail);
        await SendEmailAsync(toEmail, "Welcome to PayLibre", html, ct);
    }

    public async Task SendLoginCodeAsync(string toEmail, string code, CancellationToken ct = default)
    {
        var html = $"<p>Your PayLibre sign-in code is:</p><p style=\"font-size:22px;font-weight:bold;letter-spacing:3px\">{code}</p>"
            + "<p>It expires shortly. If you didn't try to sign in, ignore this email.</p>";
        logger.LogInformation("Login code for {Email}", toEmail);
        await SendEmailAsync(toEmail, "Your PayLibre sign-in code", html, ct);
    }

    public async Task SendPaymentReceiptAsync(
        string toName, string? email, string? phone, string studentName,
        long amountKobo, int invoicesSettled, long outstandingKobo, DateTimeOffset occurredAtUtc, CancellationToken ct = default)
    {
        string Naira(long kobo) => "₦" + (kobo / 100m).ToString("N2");
        var when = occurredAtUtc.ToString("dd MMM yyyy, HH:mm 'UTC'");
        var settledLine = invoicesSettled > 0
            ? $"{invoicesSettled} fee{(invoicesSettled == 1 ? "" : "s")} fully paid."
            : "Applied to outstanding fees.";
        var outstandingLine = outstandingKobo > 0
            ? $"Remaining balance: {Naira(outstandingKobo)}."
            : "All fees are now paid — thank you!";
        var text = $"Payment received for {studentName}: {Naira(amountKobo)} on {when}. {settledLine} {outstandingLine}";
        var subject = $"Payment received for {studentName}";
        var html = $"<p>Dear {toName},</p><p>We've received a payment for <b>{studentName}</b>.</p>"
            + $"<table style=\"border-collapse:collapse\">"
            + $"<tr><td style=\"padding:2px 12px 2px 0\">Amount</td><td><b>{Naira(amountKobo)}</b></td></tr>"
            + $"<tr><td style=\"padding:2px 12px 2px 0\">Date</td><td>{when}</td></tr>"
            + $"<tr><td style=\"padding:2px 12px 2px 0\">Fees settled</td><td>{invoicesSettled}</td></tr>"
            + $"<tr><td style=\"padding:2px 12px 2px 0\">Outstanding</td><td>{Naira(outstandingKobo)}</td></tr>"
            + $"</table><p>{outstandingLine}</p>";

        logger.LogInformation("Payment receipt for {Student} -> {Email}/{Phone}: {Amount} kobo, {Settled} settled, {Outstanding} outstanding",
            studentName, email ?? "-", phone ?? "-", amountKobo, invoicesSettled, outstandingKobo);

        if (!string.IsNullOrWhiteSpace(email)) await SendEmailAsync(email!, subject, html, ct);
        if (!string.IsNullOrWhiteSpace(phone)) await SendSmsAsync(phone!, text, ct);
    }

    public async Task SendFeeReminderAsync(
        string toName, string? email, string? phone, string studentName, string feeName,
        long outstandingKobo, DateTimeOffset dueDateUtc, bool overdue, CancellationToken ct = default)
    {
        string Naira(long kobo) => "₦" + (kobo / 100m).ToString("N2");
        var due = dueDateUtc.ToString("dd MMM yyyy");
        var lead = overdue
            ? $"The fee \"{feeName}\" for {studentName} is overdue (was due {due})."
            : $"The fee \"{feeName}\" for {studentName} is due on {due}.";
        var text = $"{lead} Outstanding: {Naira(outstandingKobo)}. Please pay into the student's PayLibre account.";
        var subject = overdue ? $"Overdue fee for {studentName}" : $"Fee reminder for {studentName}";
        var html = $"<p>Dear {toName},</p><p>{lead}</p>"
            + $"<p>Outstanding balance: <b>{Naira(outstandingKobo)}</b>.</p>"
            + "<p>Please transfer into the student's PayLibre fee account — payments are applied automatically.</p>";

        logger.LogInformation("Fee reminder ({Kind}) for {Student} -> {Email}/{Phone}: {Fee}, {Outstanding} outstanding",
            overdue ? "overdue" : "upcoming", studentName, email ?? "-", phone ?? "-", feeName, outstandingKobo);

        if (!string.IsNullOrWhiteSpace(email)) await SendEmailAsync(email!, subject, html, ct);
        if (!string.IsNullOrWhiteSpace(phone)) await SendSmsAsync(phone!, text, ct);
    }

    public async Task SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken ct = default)
    {
        var html = $"<p>Reset your PayLibre password using the link below (valid for a short time):</p>"
            + $"<p><a href=\"{resetUrl}\">{resetUrl}</a></p><p>If you didn't request this, ignore this email.</p>";
        logger.LogInformation("Password reset link for {Email}", toEmail);
        await SendEmailAsync(toEmail, "Reset your PayLibre password", html, ct);
    }

    private async Task SendEmailAsync(string to, string subject, string html, CancellationToken ct)
    {
        if (!_resend.IsConfigured) { logger.LogInformation("[email skipped — Resend not configured] to={To} subject={Subject}", to, subject); return; }
        try
        {
            var http = httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resend.ApiKey);
            req.Content = JsonContent.Create(new
            {
                from = $"{_resend.FromName} <{_resend.FromEmail}>",
                to = new[] { to },
                subject,
                html,
            });
            var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                logger.LogWarning("Resend email to {To} failed: {Status}", to, res.StatusCode);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Resend email to {To} errored", to); }
    }

    private async Task SendSmsAsync(string to, string message, CancellationToken ct)
    {
        if (!_sms.IsConfigured) { logger.LogInformation("[sms skipped — provider not configured] to={To}", to); return; }
        try
        {
            var http = httpFactory.CreateClient();
            HttpResponseMessage res;
            if (_sms.Provider.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
            {
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"https://api.twilio.com/2010-04-01/Accounts/{_sms.AccountSid}/Messages.json");
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_sms.AccountSid}:{_sms.ApiKey}")));
                req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["To"] = to, ["From"] = _sms.SenderId, ["Body"] = message });
                res = await http.SendAsync(req, ct);
            }
            else // Termii
            {
                res = await http.PostAsJsonAsync("https://api.ng.termii.com/api/sms/send", new
                {
                    to,
                    from = _sms.SenderId,
                    sms = message,
                    type = "plain",
                    channel = "generic",
                    api_key = _sms.ApiKey,
                }, ct);
            }
            if (!res.IsSuccessStatusCode)
                logger.LogWarning("SMS to {To} via {Provider} failed: {Status}", to, _sms.Provider, res.StatusCode);
        }
        catch (Exception ex) { logger.LogWarning(ex, "SMS to {To} errored", to); }
    }
}
