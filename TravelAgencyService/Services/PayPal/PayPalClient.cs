using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TravelAgencyService.Services.PayPal;

public class PayPalClient
{
    private readonly HttpClient _http;
    private readonly PayPalOptions _opt;

    public PayPalClient(HttpClient http, IOptions<PayPalOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    private string BaseUrl =>
        string.Equals(_opt.Mode, "Live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.ClientId) || string.IsNullOrWhiteSpace(_opt.ClientSecret))
            throw new InvalidOperationException("PayPal ClientId/ClientSecret missing.");

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_opt.ClientId}:{_opt.ClientSecret}"));
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        var res = await _http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new Exception($"PayPal token error: {res.StatusCode} {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()
               ?? throw new Exception("PayPal access_token missing.");
    }

    public async Task<(string orderId, string approveUrl)> CreateOrderAndGetApproveUrlAsync(
        decimal amount,
        string currency,
        string returnUrl,
        string cancelUrl,
        CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);

        var payload = new
        {
            intent = "CAPTURE",
            application_context = new
            {
                return_url = returnUrl,
                cancel_url = cancelUrl
            },
            purchase_units = new[]
            {
                new
                {
                    amount = new
                    {
                        currency_code = currency,
                        value = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                    }
                }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/checkout/orders");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var res = await _http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new Exception($"PayPal create order failed: {res.StatusCode} {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var orderId = root.GetProperty("id").GetString() ?? throw new Exception("Missing order id.");

        // מחפשים את approve link מתוך links
        string? approve = null;
        foreach (var link in root.GetProperty("links").EnumerateArray())
        {
            var rel = link.GetProperty("rel").GetString();
            if (string.Equals(rel, "approve", StringComparison.OrdinalIgnoreCase))
            {
                approve = link.GetProperty("href").GetString();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(approve))
            throw new Exception("PayPal approve link missing.");

        return (orderId, approve!);
    }

    public async Task<JsonElement> CaptureOrderAsync(string orderId, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/checkout/orders/{orderId}/capture");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var res = await _http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new Exception($"PayPal capture failed: {res.StatusCode} {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
