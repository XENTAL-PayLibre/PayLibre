using Microsoft.Extensions.Logging;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Infrastructure.Notifications;

/// <summary>
/// Placeholder notification sender: logs the delivery. Swap for a real SMS/email provider
/// (e.g. Resend + an SMS gateway) in a later phase — the interface stays the same.
/// </summary>
public sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public Task SendVirtualAccountDetailsAsync(
        string toName, string? email, string? phone, string studentName,
        string nuban, string bankName, string accountName, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Account details for {Student} → guardian {Guardian} ({Email}/{Phone}): {Bank} {Nuban} ({AccountName})",
            studentName, toName, email ?? "-", phone ?? "-", bankName, nuban, accountName);
        return Task.CompletedTask;
    }
}
