using System.Net.Http;
using System.Text.Json;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Cli;

public sealed class DashboardClient
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public async Task<bool> IsReachableAsync(Uri baseUri, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(baseUri, "/api/status"), cancellationToken).ConfigureAwait(false);
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
