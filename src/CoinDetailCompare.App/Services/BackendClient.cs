using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CoinDetailCompare.App.Models;

namespace CoinDetailCompare.App.Services;

/// <summary>Thin HTTP client for the local Python backend (only lightweight JSON).</summary>
public class BackendClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public BackendClient(string baseUrl)
    {
        // Anomaly detection on CPU can take a minute or two (patch-level
        // embeddings); keep the timeout generous rather than flaky.
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(300) };
    }

    public Task<HealthDto?> HealthAsync()
        => _http.GetFromJsonAsync<HealthDto>("/health", Opts);

    public async Task<PreprocessResponse?> PreprocessAsync(PreprocessRequest req)
        => await ContinueAsync<PreprocessResponse>(_http.PostAsJsonAsync("/preprocess", req, Opts));

    public async Task<SearchResponse?> SearchAsync(SearchRequest req)
        => await ContinueAsync<SearchResponse>(_http.PostAsJsonAsync("/search", req, Opts));

    public async Task<EmbedResponse?> EmbedAsync(EmbedRequest req)
        => await ContinueAsync<EmbedResponse>(_http.PostAsJsonAsync("/embed", req, Opts));

    public async Task<AnomalyResponse?> AnomalyAsync(AnomalyRequest req)
        => await ContinueAsync<AnomalyResponse>(_http.PostAsJsonAsync("/anomaly", req, Opts));

    public async Task<NumistaSearchResponse?> NumistaSearchAsync(NumistaSearchRequest req)
        => await ContinueAsync<NumistaSearchResponse>(_http.PostAsJsonAsync("/numista/search", req, Opts));

    public async Task<ReferencesResponse?> ReferencesAsync(string? kind = null)
    {
        var url = "/references" + (kind == null ? "" : $"?kind={Uri.EscapeDataString(kind)}");
        return await _http.GetFromJsonAsync<ReferencesResponse>(url, Opts);
    }

    public async Task<Dictionary<string, int>?> RebuildIndexAsync()
        => await ContinueAsync<Dictionary<string, int>>(_http.PostAsync("/index/rebuild", null));

    public async Task<NumistaParseResponse?> ParseNumistaLinkAsync(string link, bool fetchMeta = false)
        => await ContinueAsync<NumistaParseResponse>(_http.PostAsJsonAsync("/numista/parse", new { link, fetch_meta = fetchMeta }, Opts));

    private static async Task<T?> ContinueAsync<T>(Task<HttpResponseMessage> task)
    {
        var resp = await task;
        if (!resp.IsSuccessStatusCode)
            return default;
        return await resp.Content.ReadFromJsonAsync<T>(Opts);
    }

    public void Dispose() => _http.Dispose();
}
