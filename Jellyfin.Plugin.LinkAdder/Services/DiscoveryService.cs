using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.LinkAdder.Models;

namespace Jellyfin.Plugin.LinkAdder.Services;

public sealed class DiscoveryService
{
    private readonly IHttpClientFactory _clients;

    public DiscoveryService(IHttpClientFactory clients) => _clients = clients;

    public async Task<IReadOnlyList<DiscoveryTitle>> DiscoverAsync(Configuration.AutomationOptions options, CancellationToken ct)
    {
        var tasks = new List<Task<IReadOnlyList<DiscoveryTitle>>>();
        if (!string.IsNullOrWhiteSpace(options.TmdbCredential))
        {
            if (options.TrendingMovies) tasks.Add(GetTmdbAsync("trending/movie/week", "Trending movies", "movie", options, ct));
            if (options.NewMovies) tasks.Add(GetTmdbAsync("movie/now_playing", "New movies", "movie", options, ct));
            if (options.TopMovies) tasks.Add(GetTmdbAsync("movie/top_rated", "Top-rated movies", "movie", options, ct));
            if (options.TrendingSeries) tasks.Add(GetTmdbAsync("trending/tv/week", "Trending series", "series", options, ct));
            if (options.NewSeries) tasks.Add(GetTmdbAsync("tv/on_the_air", "New series", "series", options, ct));
            if (options.TopSeries) tasks.Add(GetTmdbAsync("tv/top_rated", "Top-rated series", "series", options, ct));
        }
        if (options.TrendingAnime) tasks.Add(GetAniListAsync(options, ct));
        if (tasks.Count == 0) return [];
        var groups = await Task.WhenAll(tasks).ConfigureAwait(false);
        return groups.SelectMany(x => x).GroupBy(x => x.ContentId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray();
    }

    private async Task<IReadOnlyList<DiscoveryTitle>> GetTmdbAsync(string endpoint, string category, string mediaType, Configuration.AutomationOptions options, CancellationToken ct)
    {
        using var client = _clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Local-ISP-FTP-Loader/2.5");
        var credential = options.TmdbCredential.Trim();
        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var url = $"https://api.themoviedb.org/3/{endpoint}{separator}language=en-US&page=1";
        if (credential.StartsWith("eyJ", StringComparison.Ordinal))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        else
            url += "&api_key=" + Uri.EscapeDataString(credential);

        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var items = new List<DiscoveryTitle>();
        foreach (var node in root?["results"]?.AsArray() ?? [])
        {
            if (node is null) continue;
            var rating = node["vote_average"]?.GetValue<double>() ?? 0;
            var votes = node["vote_count"]?.GetValue<int>() ?? 0;
            if (rating < options.MinimumRating || votes < options.MinimumVotes) continue;
            var id = node["id"]?.GetValue<int>() ?? 0;
            var title = node[mediaType == "movie" ? "title" : "name"]?.GetValue<string>() ?? string.Empty;
            var original = node[mediaType == "movie" ? "original_title" : "original_name"]?.GetValue<string>() ?? title;
            var date = node[mediaType == "movie" ? "release_date" : "first_air_date"]?.GetValue<string>();
            int? year = DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed.Year : null;
            var poster = node["poster_path"]?.GetValue<string>();
            if (id == 0 || title.Length == 0) continue;
            items.Add(new($"tmdb:{mediaType}:{id}", "TMDb", category, mediaType, title, original, year, rating, votes,
                string.IsNullOrWhiteSpace(poster) ? string.Empty : "https://image.tmdb.org/t/p/w342" + poster));
            if (items.Count >= options.MaxPerCategory) break;
        }
        return items;
    }

    private async Task<IReadOnlyList<DiscoveryTitle>> GetAniListAsync(Configuration.AutomationOptions options, CancellationToken ct)
    {
        const string query = "query ($perPage: Int) { Page(page: 1, perPage: $perPage) { media(type: ANIME, sort: TRENDING_DESC, isAdult: false) { id title { romaji english native } startDate { year } averageScore popularity coverImage { large } } } }";
        using var client = _clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Local-ISP-FTP-Loader/2.5");
        using var content = new StringContent(JsonSerializer.Serialize(new { query, variables = new { perPage = Math.Clamp(options.MaxPerCategory * 3, 10, 50) } }), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("https://graphql.anilist.co", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var items = new List<DiscoveryTitle>();
        foreach (var node in root?["data"]?["Page"]?["media"]?.AsArray() ?? [])
        {
            if (node is null) continue;
            var score = (node["averageScore"]?.GetValue<double>() ?? 0) / 10d;
            var popularity = node["popularity"]?.GetValue<int>() ?? 0;
            if (score < options.MinimumRating) continue;
            var id = node["id"]?.GetValue<int>() ?? 0;
            var english = node["title"]?["english"]?.GetValue<string>();
            var romaji = node["title"]?["romaji"]?.GetValue<string>() ?? string.Empty;
            var title = string.IsNullOrWhiteSpace(english) ? romaji : english;
            if (id == 0 || title.Length == 0) continue;
            items.Add(new($"anilist:anime:{id}", "AniList", "Trending anime", "anime", title, romaji,
                node["startDate"]?["year"]?.GetValue<int>(), score, popularity, node["coverImage"]?["large"]?.GetValue<string>() ?? string.Empty));
            if (items.Count >= options.MaxPerCategory) break;
        }
        return items;
    }
}
