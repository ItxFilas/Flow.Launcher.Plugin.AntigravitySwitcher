using System.Net.Http;
using System.Text.Json;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Cli;

public sealed class DashboardClient : IDisposable
{
    private readonly HttpClient _http;

    public DashboardClient(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler())
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    public static readonly TimeSpan FastProbeTimeout = TimeSpan.FromMilliseconds(350);

    public Task<bool> IsReachableAsync(Uri baseUri, CancellationToken cancellationToken = default) =>
        IsReachableAsync(baseUri, FastProbeTimeout, cancellationToken);

    public async Task<bool> IsReachableAsync(Uri baseUri, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(timeout);

        try
        {
            using var response = await _http.GetAsync(new Uri(baseUri, "/api/status"), probeCts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetAccountsJsonAsync(Uri baseUri, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(baseUri, "/api/accounts"), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SelectAccountAsync(Uri baseUri, string accountId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsync(
                new Uri(baseUri, $"/api/accounts/{Uri.EscapeDataString(accountId)}/select"),
                content: null,
                cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteAccountAsync(Uri baseUri, string accountId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.DeleteAsync(
                new Uri(baseUri, $"/api/accounts/{Uri.EscapeDataString(accountId)}"),
                cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RefreshQuotasAsync(Uri baseUri, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsync(
                new Uri(baseUri, "/api/quota/refresh"),
                content: null,
                cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Rewrites Antigravity's login credential so the IDE signs in as the given account
    /// (POST /api/accounts/{id}/login-swap on the switcher daemon).
    /// </summary>
    public async Task<(bool Succeeded, string? Error)> LoginSwapAsync(Uri baseUri, string accountId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsync(
                new Uri(baseUri, $"/api/accounts/{Uri.EscapeDataString(accountId)}/login-swap"),
                content: null,
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (false, TryReadErrorMessage(body) ?? $"The switcher returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Reads the {"error":{"code","message","detail"}} payload used by the switcher API.</summary>
    public static string? TryReadErrorMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
            var detail = error.TryGetProperty("detail", out var d) ? d.GetString() : null;

            if (!string.IsNullOrWhiteSpace(message) &&
                !string.IsNullOrWhiteSpace(detail) &&
                !string.Equals(message, detail, StringComparison.Ordinal))
            {
                return $"{message} {detail}".Trim();
            }

            return string.IsNullOrWhiteSpace(message) ? detail : message;
        }
        catch
        {
            return null;
        }
    }

    public static bool LooksLikeJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.TrimStart();
        return trimmed.StartsWith('[') || trimmed.StartsWith('{');
    }

    public static bool TryReadSuccess(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("success", out var success) &&
                   success.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }
}