using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpokysProjectLightning.Models;

namespace SpokysProjectLightning.Services
{
    public class MovieScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly string _tmdbApiKey;
        private readonly string _omdbApiKey;
        private const string BaseUrl = "https://api.themoviedb.org/3";

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
            _tmdbApiKey = string.IsNullOrEmpty(settings.TmdbApiKey) ? "03ea17fd725585fa30751965ed1993eb" : settings.TmdbApiKey;
            _omdbApiKey = settings.OmdbApiKey;
        }

        public async Task<List<MovieResult>> GetTrendingAsync()
            => await FetchMoviesAsync($"{BaseUrl}/trending/all/week?api_key={_tmdbApiKey}&language=en-US&page=1");

        public async Task<List<MovieResult>> GetPopularMoviesAsync()
            => await FetchMoviesAsync($"{BaseUrl}/movie/popular?api_key={_tmdbApiKey}&language=en-US&page=1");

        public async Task<List<MovieResult>> GetTopRatedAsync()
            => await FetchMoviesAsync($"{BaseUrl}/movie/top_rated?api_key={_tmdbApiKey}&language=en-US&page=1");

        public async Task<List<MovieResult>> GetNowPlayingAsync()
            => await FetchMoviesAsync($"{BaseUrl}/movie/now_playing?api_key={_tmdbApiKey}&language=en-US&page=1");

        public async Task<List<MovieResult>> GetUpcomingAsync()
            => await FetchMoviesAsync($"{BaseUrl}/movie/upcoming?api_key={_tmdbApiKey}&language=en-US&page=1");

        public async Task<List<MovieResult>> GetOnTVAsync()
            => await FetchMoviesAsync($"{BaseUrl}/tv/on_the_air?api_key={_tmdbApiKey}&language=en-US&page=1");

        /// <summary>
        /// Get recommended movies - combines popular and trending, deduped
        /// </summary>
        public async Task<List<MovieResult>> GetRecommendedAsync()
        {
            // Fetch popular + trending in parallel for a faster "recommended" feed
            var popularTask = GetPopularMoviesAsync();
            var trendingTask = GetTrendingAsync();
            await Task.WhenAll(popularTask, trendingTask);
            var popular = await popularTask;
            var trending = await trendingTask;

            // Merge and deduplicate by ID, keep highest rated
            var merged = popular.Concat(trending)
                .GroupBy(m => m.Id)
                .Select(g => g.OrderByDescending(m => m.VoteAverage).First())
                .OrderByDescending(m => m.VoteAverage)
                .Take(20)
                .ToList();

            return merged;
        }

        public async Task<List<MovieResult>> SearchMoviesAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new();

            // Primary: VidLink search (no API key needed — works for everyone)
            try
            {
                var vidlink = await SearchVidLinkAsync(query);
                if (vidlink.Count > 0) return vidlink;
            }
            catch { }

            // Fallback: TMDB search
            try
            {
                var tmdb = await FetchMoviesAsync(
                    $"{BaseUrl}/search/multi?api_key={_tmdbApiKey}&language=en-US&query={Uri.EscapeDataString(query)}&page=1&include_adult=false");
                if (tmdb.Count > 0) return tmdb;
            }
            catch { }

            // Fallback: OMDb search + convert IMDb IDs to TMDB IDs
            try
            {
                var omdb = await SearchOmdbAsync(query, "");
                if (omdb.Count > 0)
                {
                    // Try to resolve IMDb IDs to real TMDB IDs
                    var resolved = new List<MovieResult>();
                    foreach (var o in omdb.Take(10))
                    {
                        var tmdbId = await ImdbToTmdbAsync(o.ImdbId);
                        if (tmdbId > 0)
                        {
                            o.Id = tmdbId;
                            resolved.Add(o);
                        }
                    }
                    if (resolved.Count > 0) return resolved;
                }
            }
            catch { }

            // Fallback: hardcoded catalogue
            var local = SearchCachedMovies(query);
            if (local.Count > 0) return local;

            return new List<MovieResult>();
        }

        private async Task<int> ImdbToTmdbAsync(string imdbId)
        {
            if (string.IsNullOrEmpty(imdbId)) return 0;
            try
            {
                var url = $"{BaseUrl}/find/{imdbId}?api_key={_tmdbApiKey}&external_source=imdb_id";
                var json = await _httpClient.GetStringAsync(url);
                var data = JObject.Parse(json);
                var movieResults = data["movie_results"] as JArray;
                if (movieResults != null && movieResults.Count > 0)
                    return movieResults[0]["id"]?.Value<int>() ?? 0;
                var tvResults = data["tv_results"] as JArray;
                if (tvResults != null && tvResults.Count > 0)
                    return tvResults[0]["id"]?.Value<int>() ?? 0;
            }
            catch { }
            return 0;
        }

        private async Task<List<MovieResult>> SearchOmdbAsync(string query, string type)
        {
            if (string.IsNullOrWhiteSpace(_omdbApiKey))
                return new List<MovieResult>();

            var typeParam = string.IsNullOrEmpty(type) ? "" : $"&type={type}";
            var searchUrl = $"https://www.omdbapi.com/?apikey={Uri.EscapeDataString(_omdbApiKey)}&s={Uri.EscapeDataString(query)}{typeParam}";
            var json = await _httpClient.GetStringAsync(searchUrl);
            var data = JObject.Parse(json);
            
            // Check for OMDB errors
            if (data["Response"]?.Value<string>() == "False")
                return new List<MovieResult>();
            
            var searchResults = data["Search"] as JArray;
            if (searchResults != null)
            {
                return searchResults.Select(s => new MovieResult
                {
                    Id = 0, // will be resolved via TMDB find endpoint
                    Title = s["Title"]?.Value<string>() ?? "Unknown",
                    PosterPath = s["Poster"]?.Value<string>()?.Replace("http://", "https://") ?? "",
                    ReleaseDate = (s["Year"]?.Value<string>() ?? "") + "-01-01",
                    MediaType = type == "series" ? "tv" : "movie",
                    ImdbId = s["imdbID"]?.Value<string>() ?? "",
                }).Where(m => !string.IsNullOrEmpty(m.ImdbId)).Take(20).ToList();
            }
            return new List<MovieResult>();
        }

        private static List<MovieResult> SearchCachedMovies(string query)
        {
            var q = query.ToLowerInvariant().Trim();
            return _cachedMovies
                .Where(m => m.Title.ToLowerInvariant().Contains(q))
                .Take(20)
                .ToList();
        }

        private static readonly List<MovieResult> _cachedMovies = new()
        {
            new() { Id = 550, Title = "Fight Club", PosterPath = "/pB8BM7pdSp6B6Ih7QZ4DrQ3PmJK.jpg", MediaType = "movie", VoteAverage = 8.4 },
            new() { Id = 680, Title = "Pulp Fiction", PosterPath = "/d5iIlFn5s0ImszYzBPb8JPIfbXD.jpg", MediaType = "movie", VoteAverage = 8.5 },
            new() { Id = 238, Title = "The Godfather", PosterPath = "/3bhkrj58Vtu7enYsRolD1fZdja1.jpg", MediaType = "movie", VoteAverage = 8.7 },
            new() { Id = 240, Title = "The Godfather Part II", PosterPath = "/hek3koDUyRQk7FIhPXsa6mT2Zc3.jpg", MediaType = "movie", VoteAverage = 8.6 },
            new() { Id = 155, Title = "The Dark Knight", PosterPath = "/qJ2tW6WMUDux911BytUr38Mt8dT.jpg", MediaType = "movie", VoteAverage = 8.5 },
            new() { Id = 497, Title = "The Green Mile", PosterPath = "/velWPhVMQeQKcxggNEU8YmIo52R.jpg", MediaType = "movie", VoteAverage = 8.5 },
            new() { Id = 424, Title = "Schindler's List", PosterPath = "/sF1U4EUQS8YHUYjNl3pMGNIQyr0.jpg", MediaType = "movie", VoteAverage = 8.6 },
            new() { Id = 389, Title = "12 Angry Men", PosterPath = "/ppd84D2i9W8jXmsyInGyihiSyqz.jpg", MediaType = "movie", VoteAverage = 8.5 },
            new() { Id = 129, Title = "Spirited Away", PosterPath = "/39wmItIWsg5sZMyRUHLkWBcuVCM.jpg", MediaType = "movie", VoteAverage = 8.5 },
            new() { Id = 122, Title = "The Lord of the Rings: The Return of the King", PosterPath = "/rCzpDGLbOoPwLjy3OAm5NUPOTrC.jpg", MediaType = "movie", VoteAverage = 8.5 },
            new() { Id = 120, Title = "The Lord of the Rings: The Fellowship of the Ring", PosterPath = "/6oom5QYQ2yQTM4W2Wj4iW9HUFGZ.jpg", MediaType = "movie", VoteAverage = 8.4 },
            new() { Id = 121, Title = "The Lord of the Rings: The Two Towers", PosterPath = "/5VTN0pR8gcYV1W7C4bP1FH6qpMA.jpg", MediaType = "movie", VoteAverage = 8.4 },
            new() { Id = 27205, Title = "Inception", PosterPath = "/oYuLEt3zVCKq57qu2F8dT7NIa6f.jpg", MediaType = "movie", VoteAverage = 8.4 },
            new() { Id = 807, Title = "Se7en", PosterPath = "/6yoghtyTpznpBik8EngEmJskVUO.jpg", MediaType = "movie", VoteAverage = 8.3 },
            new() { Id = 278, Title = "The Shawshank Redemption", PosterPath = "/9cjIGRQL1m4E87FkTJk1wrcxLY.jpg", MediaType = "movie", VoteAverage = 8.7 },
            new() { Id = 769, Title = "Goodfellas", PosterPath = "/aKuFiU82s5ISJDx3d5M6SEFGGU.jpg", MediaType = "movie", VoteAverage = 8.5 },
            new() { Id = 1124, Title = "The Prestige", PosterPath = "/bdN3gXuIZYaJP7ftKK2sU0nPtEA.jpg", MediaType = "movie", VoteAverage = 8.2 },
            new() { Id = 603, Title = "The Matrix", PosterPath = "/f89U3ADr1oiB1s9GkdPOEpXUk5H.jpg", MediaType = "movie", VoteAverage = 8.2 },
            new() { Id = 510, Title = "One Flew Over the Cuckoo's Nest", PosterPath = "/3jcbDmRFiQ83drXNO8fVyyT0TVM.jpg", MediaType = "movie", VoteAverage = 8.4 },
            new() { Id = 311, Title = "The Silence of the Lambs", PosterPath = "/uS9m8OBk1RYF5Jc3bGQmxN2j8h.jpg", MediaType = "movie", VoteAverage = 8.3 },
            new() { Id = 157336, Title = "Interstellar", PosterPath = "/gEU2QniE6E77NI6lCU6MxlNBvIx.jpg", MediaType = "movie", VoteAverage = 8.4 },
            new() { Id = 550988, Title = "Free Guy", PosterPath = "/xmbU4JTjNXCFQJ4MxzSJsYy3w2.jpg", MediaType = "movie", VoteAverage = 7.8 },
            new() { Id = 361743, Title = "Top Gun: Maverick", PosterPath = "/62HCnUTPRrGZ1BCiV1D7ZGTgnQo.jpg", MediaType = "movie", VoteAverage = 8.3 },
            new() { Id = 524434, Title = "The Eternals", PosterPath = "/bcCBq9N1EMo3daNIjWJHWk5B0d.jpg", MediaType = "movie", VoteAverage = 7.1 },
            new() { Id = 438631, Title = "Dune", PosterPath = "/d5NXSklXo0qyIYkgV94XAgMIckC.jpg", MediaType = "movie", VoteAverage = 7.8 },
            new() { Id = 299536, Title = "Avengers: Infinity War", PosterPath = "/7WsyChQLEftFiDOVTGkv3hFpyyt.jpg", MediaType = "movie", VoteAverage = 8.3 },
            new() { Id = 244786, Title = "Whiplash", PosterPath = "/7fn624j5lj3xTme2SgiLCeuedmO.jpg", MediaType = "movie", VoteAverage = 8.4 },
            new() { Id = 49026, Title = "The Dark Knight Rises", PosterPath = "/hr0L2aueqlP2BYUblTTjmtn0hw4.jpg", MediaType = "movie", VoteAverage = 7.8 },
            new() { Id = 9340, Title = "The Magnificent Seven", PosterPath = "/z6BP8yL1uPI12N3ufqwSPqgEmM2.jpg", MediaType = "movie", VoteAverage = 7.0 },
            new() { Id = 335984, Title = "Blade Runner 2049", PosterPath = "/gajva2L0rPYCHZMVCq9A3BDC4PM.jpg", MediaType = "movie", VoteAverage = 7.6 },
            new() { Id = 500, Title = "Reservoir Dogs", PosterPath = "/xi8Iu6qyTfyD5jbKFfeSEx2pAFO.jpg", MediaType = "movie", VoteAverage = 8.1 },
            new() { Id = 19, Title = "Metropolis", PosterPath = "/qri8G3h8Te4ak7i3KM3H7FMYH6z.jpg", MediaType = "movie", VoteAverage = 8.2 },
            new() { Id = 13, Title = "Forrest Gump", PosterPath = "/arw2vcBveWOVZr6pxd9XTd1TdQa.jpg", MediaType = "movie", VoteAverage = 8.5 },
            new() { Id = 640, Title = "Catch Me If You Can", PosterPath = "/ctjE21H2hGnRZWKHmO1U5SZISFp.jpg", MediaType = "movie", VoteAverage = 7.8 },
            new() { Id = 207, Title = "Dead Poets Society", PosterPath = "/aiZkDsu4w8wY1uY1gVaC7bnuMAz.jpg", MediaType = "movie", VoteAverage = 8.3 },
            new() { Id = 752, Title = "V for Vendetta", PosterPath = "/5uCxSC3SWnEDvqE3Rg2Vp7GVpnl.jpg", MediaType = "movie", VoteAverage = 8.0 },
            new() { Id = 118340, Title = "Guardians of the Galaxy", PosterPath = "/y31QB9kn3XSudA15tV7F1U6f8Mh.jpg", MediaType = "movie", VoteAverage = 7.8 },
            new() { Id = 76341, Title = "Mad Max: Fury Road", PosterPath = "/8tZYtuWezp8Jk1O01nPEjYxZz3M.jpg", MediaType = "movie", VoteAverage = 7.6 },
            new() { Id = 185, Title = "A Clockwork Orange", PosterPath = "/4sHeTAp65OmU6QJ2yBPQ4PFojcY.jpg", MediaType = "movie", VoteAverage = 8.2 },
            new() { Id = 348, Title = "Alien", PosterPath = "/vfrQk5IPloGg1v9R2Jt4H3EgT9e.jpg", MediaType = "movie", VoteAverage = 8.1 },
            new() { Id = 74, Title = "War of the Worlds", PosterPath = "/6h9J3I4Atu1OxRwyVqw8HktwO2E.jpg", MediaType = "movie", VoteAverage = 6.7 },
            new() { Id = 335, Title = "Iron Man", PosterPath = "/78lPobvkV2nuSg6Lf9TjMUEg81m.jpg", MediaType = "movie", VoteAverage = 7.6 },
            new() { Id = 315162, Title = "Puss in Boots: The Last Wish", PosterPath = "/kuf6dutpsT0vSVehic3EZIqkOBt.jpg", MediaType = "movie", VoteAverage = 8.0 },
            new() { Id = 361466, Title = "The Batman", PosterPath = "/74xTEgt7R36FpoG50KiCZOOJx0y.jpg", MediaType = "movie", VoteAverage = 7.6 },
            new() { Id = 453395, Title = "Doctor Strange in the Multiverse of Madness", PosterPath = "/9Gtg2DzBydYl7JEh3FfQn60cT1.jpg", MediaType = "movie", VoteAverage = 7.3 },
            new() { Id = 616037, Title = "Thor: Love and Thunder", PosterPath = "/pjsKCb1aN1UG0KB8DlKcOdHOMjY.jpg", MediaType = "movie", VoteAverage = 6.7 },
            new() { Id = 438148, Title = "A Quiet Place Part II", PosterPath = "/haJh3YKWNVYBCF2J4Lq6TzlcfJW.jpg", MediaType = "movie", VoteAverage = 7.5 },
            new() { Id = 568124, Title = "The Suicide Squad", PosterPath = "/kb4s0MLp4z34GgZRhYevkLeKOBz.jpg", MediaType = "movie", VoteAverage = 7.5 },
            new() { Id = 460465, Title = "Mortal Kombat", PosterPath = "/nKTVLP2MqC9NkUARUsflH3s5YYf.jpg", MediaType = "movie", VoteAverage = 7.5 },
            new() { Id = 791373, Title = "Zack Snyder's Justice League", PosterPath = "/3C2E78UjKDfCJppCBhNx44tN7sG.jpg", MediaType = "movie", VoteAverage = 8.2 },
        };

        private async Task<List<MovieResult>> SearchVidLinkAsync(string query)
        {
            var results = new List<MovieResult>();
            try
            {
                var url = $"https://vidlink.pro/api/search?q={Uri.EscapeDataString(query)}";
                var json = await _httpClient.GetStringAsync(url);
                var items = JArray.Parse(json);
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
            }
            catch { }
            return results;
        }

        private async Task<List<MovieResult>> FetchMoviesAsync(string url)
        {
            var results = new List<MovieResult>();
            try
            {
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB Error: {ex.Message}");
            }
            return results;
        }

        /// <summary>
        /// Get all embed URLs for a movie/show, ordered by reliability (most likely to work first)
        /// </summary>
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

        /// <summary>
        /// Get the primary embed URL (best source first)
        /// </summary>
        public string GetEmbedUrl(int tmdbId, string mediaType = "movie", int season = 1, int episode = 1)
        {
            return GetAllEmbedUrls(tmdbId, mediaType, season, episode)[0];
        }

        public async Task<List<int>> GetSeasonsAsync(int tmdbId)
        {
            var seasons = new List<int>();
            try
            {
                var response = await _httpClient.GetStringAsync($"{BaseUrl}/tv/{tmdbId}?api_key={_tmdbApiKey}&language=en-US");
                var json = JObject.Parse(response);
                var seasonsArray = json["seasons"] as JArray;
                if (seasonsArray != null)
                {
                    foreach (var s in seasonsArray)
                    {
                        int seasonNum = s["season_number"]?.Value<int>() ?? 0;
                        if (seasonNum > 0) seasons.Add(seasonNum);
                    }
                }
            }
            catch { }
            return seasons;
        }

        public async Task<List<int>> GetEpisodesAsync(int tmdbId, int season)
        {
            var episodes = new List<int>();
            try
            {
                var response = await _httpClient.GetStringAsync($"{BaseUrl}/tv/{tmdbId}/season/{season}?api_key={_tmdbApiKey}&language=en-US");
                var json = JObject.Parse(response);
                var episodesArray = json["episodes"] as JArray;
                if (episodesArray != null)
                {
                    foreach (var ep in episodesArray)
                    {
                        int epNum = ep["episode_number"]?.Value<int>() ?? 0;
                        if (epNum > 0) episodes.Add(epNum);
                    }
                }
            }
            catch { }
            return episodes;
        }
    }
}

