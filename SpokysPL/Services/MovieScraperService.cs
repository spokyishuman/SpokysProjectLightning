using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpokysProjectLightning.Models;

namespace SpokysProjectLightning.Services
{
    public class MovieScraperService
    {
        private readonly HttpClient _httpClient;
        private string TmdbBaseUrl;
        private readonly string _omdbApiKey;
        private const string DefaultTmdbBase = "https://api.themoviedb.org/3";

        public string ProxyUrl { get; private set; } = string.Empty;
        public bool UsingProxy => !string.IsNullOrEmpty(ProxyUrl);

        public MovieScraperService()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                }
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _httpClient.Timeout = TimeSpan.FromSeconds(20);

            var settings = new DataService().LoadSettings();
            var apiKey = string.IsNullOrEmpty(settings.TmdbApiKey) ? "03ea17fd725585fa30751965ed1993eb" : settings.TmdbApiKey;

            // Priority: proxy URL > direct TMDB (with user's API key) > bundled database
            ProxyUrl = settings.MovieProxyUrl?.TrimEnd('/') ?? "";
            if (!string.IsNullOrEmpty(ProxyUrl))
                TmdbBaseUrl = ProxyUrl;
            else if (!string.IsNullOrEmpty(apiKey))
                TmdbBaseUrl = $"{DefaultTmdbBase}?api_key={apiKey}";
            else
                TmdbBaseUrl = "";

            _omdbApiKey = settings.OmdbApiKey;
        }

        public string SourceLabel => UsingProxy
            ? $"☁️ Proxy ({new Uri(ProxyUrl).Host})"
            : TmdbBaseUrl.Contains("api_key=")
                ? "🎬 TMDB (direct)"
                : "📚 Local Database";

        public List<MovieResult> LoadBundled()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "movies.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<List<MovieResult>>(json) ?? new();
                }
            }
            catch { }
            return new();
        }

        public async Task<List<MovieResult>> GetTrendingAsync()
            => await FetchMoviesAsync($"trending/all/week");

        public async Task<List<MovieResult>> GetPopularMoviesAsync()
            => await FetchMoviesAsync("movie/popular");

        public async Task<List<MovieResult>> GetTopRatedAsync()
            => await FetchMoviesAsync("movie/top_rated");

        public async Task<List<MovieResult>> GetNowPlayingAsync()
            => await FetchMoviesAsync("movie/now_playing");

        public async Task<List<MovieResult>> GetUpcomingAsync()
            => await FetchMoviesAsync("movie/upcoming");

        public async Task<List<MovieResult>> GetOnTVAsync()
            => await FetchMoviesAsync("tv/on_the_air");

        public async Task<List<MovieResult>> GetRecommendedAsync()
            => await FetchMoviesAsync("trending/all/week");

        public async Task<List<MovieResult>> SearchMoviesAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new();

            // Primary: VidLink search (no API key needed)
            try
            {
                var url = $"https://vidlink.pro/api/search?q={Uri.EscapeDataString(query)}";
                var json = await _httpClient.GetStringAsync(url);
                var items = JArray.Parse(json);
                var results = new List<MovieResult>();
                foreach (var item in items)
                {
                    var id = item["id"]?.Value<int>() ?? 0;
                    if (id <= 0) continue;
                    results.Add(new MovieResult
                    {
                        Id = id,
                        Title = item["title"]?.Value<string>() ?? "Unknown",
                        PosterPath = item["image"]?.Value<string>()?.Replace("http://", "https://") ?? "",
                        MediaType = item["type"]?.Value<string>() == "show" ? "tv" : "movie",
                        Overview = item["description"]?.Value<string>() ?? "",
                        VoteAverage = item["rating"]?.Value<double>() ?? 0,
                        ReleaseDate = (item["year"]?.Value<string>() ?? "") + "-01-01",
                    });
                }
                if (results.Count > 0) return results;
            }
            catch { }

            // Fallback: TMDB/proxy search
            try
            {
                var tmdb = await FetchMoviesAsync($"search/multi&query={Uri.EscapeDataString(query)}&include_adult=false");
                if (tmdb.Count > 0) return tmdb;
            }
            catch { }

            // Fallback: bundled database filter
            var bundled = LoadBundled();
            var filtered = bundled.FindAll(m =>
                m.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
            return filtered;
        }

        public List<string> GetAllEmbedUrls(int tmdbId, string mediaType = "movie", int season = 1, int episode = 1)
        {
            var urls = new List<string>();
            if (mediaType == "tv")
            {
                urls.Add($"https://autoembed.co/tv/tmdb/{tmdbId}-{season}-{episode}");
                urls.Add($"https://vidlink.pro/tv/{tmdbId}/{season}/{episode}");
                urls.Add($"https://vidsrc.to/embed/tv/{tmdbId}/{season}/{episode}");
                urls.Add($"https://multiembed.mov/?video_id={tmdbId}&tmdb=1&s={season}&e={episode}");
                urls.Add($"https://moviesapi.to/tv/{tmdbId}/{season}/{episode}");
                urls.Add($"https://embed.su/embed/tv/{tmdbId}/{season}/{episode}");
                urls.Add($"https://2embed.cc/embedtv/{tmdbId}&s={season}&e={episode}");
                urls.Add($"https://player.superembed.stream/embed/tv/{tmdbId}/{season}/{episode}");
                urls.Add($"https://www.filmembed.com/tv/tmdb/{tmdbId}-{season}-{episode}");
                urls.Add($"https://ww1.couchtuner.cloud/embed/tv/{tmdbId}/{season}/{episode}");
            }
            else
            {
                urls.Add($"https://autoembed.co/movie/tmdb/{tmdbId}");
                urls.Add($"https://vidlink.pro/movie/{tmdbId}");
                urls.Add($"https://vidsrc.to/embed/movie?tmdb={tmdbId}");
                urls.Add($"https://multiembed.mov/?video_id={tmdbId}&tmdb=1");
                urls.Add($"https://moviesapi.to/movie/{tmdbId}");
                urls.Add($"https://embed.su/embed/movie/{tmdbId}");
                urls.Add($"https://2embed.cc/embed/{tmdbId}");
                urls.Add($"https://player.superembed.stream/embed/movie/{tmdbId}");
                urls.Add($"https://www.filmembed.com/movie/tmdb/{tmdbId}");
                urls.Add($"https://ww1.couchtuner.cloud/embed/movie/{tmdbId}");
            }
            return urls;
        }

        public async Task<List<int>> GetSeasonsAsync(int tmdbId)
        {
            try
            {
                var response = await FetchRawAsync($"tv/{tmdbId}");
                var json = JObject.Parse(response);
                var seasons = json["seasons"] as JArray;
                if (seasons == null) return new();
                var result = new List<int>();
                foreach (var s in seasons)
                {
                    var num = s["season_number"]?.Value<int>() ?? 0;
                    if (num > 0) result.Add(num);
                }
                return result;
            }
            catch { return new(); }
        }

        public async Task<List<int>> GetEpisodesAsync(int tmdbId, int season)
        {
            try
            {
                var response = await FetchRawAsync($"tv/{tmdbId}/season/{season}");
                var json = JObject.Parse(response);
                var episodes = json["episodes"] as JArray;
                if (episodes == null) return new();
                var result = new List<int>();
                foreach (var ep in episodes)
                {
                    var num = ep["episode_number"]?.Value<int>() ?? 0;
                    if (num > 0) result.Add(num);
                }
                return result;
            }
            catch { return new(); }
        }

        private async Task<List<MovieResult>> FetchMoviesAsync(string endpoint)
        {
            var results = new List<MovieResult>();
            if (string.IsNullOrEmpty(TmdbBaseUrl)) return results;
            try
            {
                var url = $"{TmdbBaseUrl}/{endpoint}";
                if (!url.Contains("?")) url += "?";
                if (!url.EndsWith("?") && !url.EndsWith("&")) url += "&";
                url += "language=en-US&page=1";

                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                var items = json["results"] as JArray;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        string mediaType = item["media_type"]?.Value<string>() ??
                                          (item["first_air_date"] != null ? "tv" : "movie");
                        if (mediaType == "person") continue;

                        var movie = new MovieResult
                        {
                            Id = item["id"]?.Value<int>() ?? 0,
                            Title = item["title"]?.Value<string>() ?? item["name"]?.Value<string>() ?? "Unknown",
                            PosterPath = item["poster_path"]?.Value<string>() ?? "",
                            BackdropPath = item["backdrop_path"]?.Value<string>() ?? "",
                            Overview = item["overview"]?.Value<string>() ?? "",
                            VoteAverage = item["vote_average"]?.Value<double>() ?? 0,
                            ReleaseDate = item["release_date"]?.Value<string>() ?? item["first_air_date"]?.Value<string>() ?? "",
                            MediaType = mediaType,
                            NumberSeasons = item["number_of_seasons"]?.Value<int>() ?? 0,
                            NumberEpisodes = item["number_of_episodes"]?.Value<int>() ?? 0
                        };

                        if (movie.Id > 0 && !string.IsNullOrEmpty(movie.PosterPath))
                            results.Add(movie);
                    }
                }
            }
            catch { }
            return results;
        }

        private async Task<string> FetchRawAsync(string endpoint)
        {
            if (string.IsNullOrEmpty(TmdbBaseUrl)) return "{}";
            var url = $"{TmdbBaseUrl}/{endpoint}";
            return await _httpClient.GetStringAsync(url);
        }
    }
}
