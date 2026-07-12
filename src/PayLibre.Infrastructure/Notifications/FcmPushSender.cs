using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Infrastructure.Notifications;

/// <summary>
/// Sends push notifications via Firebase Cloud Messaging. Config-gated: when no server key is set the
/// message is logged and skipped, so the platform runs without push credentials (like SMS). Sends to
/// each token individually (best-effort); failures are logged, never thrown.
/// </summary>
public sealed class FcmPushSender(IHttpClientFactory httpFactory, IOptions<PushOptions> options, ILogger<FcmPushSender> logger) : IPushSender
{
    private readonly PushOptions _options = options.Value;

    public async Task SendAsync(IReadOnlyList<string> deviceTokens, string title, string body, CancellationToken ct = default)
    {
        if (deviceTokens.Count == 0) return;
        if (!_options.IsConfigured)
        {
            logger.LogInformation("[push skipped — FCM not configured] {Count} device(s): {Title}", deviceTokens.Count, title);
            return;
        }
        var http = httpFactory.CreateClient();
        foreach (var token in deviceTokens)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://fcm.googleapis.com/fcm/send");
                req.Headers.TryAddWithoutValidation("Authorization", $"key={_options.FcmServerKey}");
                req.Content = JsonContent.Create(new { to = token, notification = new { title, body } });
                using var res = await http.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode)
                    logger.LogWarning("FCM push to a device failed: {Status}", res.StatusCode);
            }
            catch (Exception ex) { logger.LogWarning(ex, "FCM push errored"); }
        }
    }
}
