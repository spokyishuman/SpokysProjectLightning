using System;
using System.Collections.Generic;

namespace SpokysProjectVercel.Models
{
    public class GameInfo
    {
        public string AppId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FixName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool LaunchSteam { get; set; }
        public bool LaunchExe { get; set; }
        public string Comments { get; set; } = string.Empty;
        public List<string> RequiredPrograms { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public string HeroImage { get; set; } = string.Empty;
        public string Background { get; set; } = string.Empty;
        public string HeaderUrl { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public class OnlineFixGame
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string LastUpdate { get; set; } = string.Empty;
        public List<DownloadLink> DownloadLinks { get; set; } = new();
        public bool IsInstalled { get; set; }
        public string LocalPath { get; set; } = string.Empty;
        public string Password { get; set; } = "online-fix.me";
        public string GameVersion { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
    }

    public class DownloadLink
    {
        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Type { get; set; } = "Download";
        public long Size { get; set; }
    }

    public class MovieInfo
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string SiteSource { get; set; } = string.Empty;
    }

    public class ManifestInfo
    {
        public string AppId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ManifestUrl { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public bool IsRecommended { get; set; }
    }

    public class MovieResult
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PosterPath { get; set; } = string.Empty;
        public string BackdropPath { get; set; } = string.Empty;
        public string Overview { get; set; } = string.Empty;
        public double VoteAverage { get; set; }
        public string ReleaseDate { get; set; } = string.Empty;
        public string MediaType { get; set; } = "movie";
        public int NumberSeasons { get; set; }
        public int NumberEpisodes { get; set; }
        public string ImdbId { get; set; } = string.Empty;
        public string PosterUrl => string.IsNullOrEmpty(PosterPath) ? "" : PosterPath.StartsWith("http") ? PosterPath : $"https://image.tmdb.org/t/p/w342{PosterPath}";
        public string BackdropUrl => string.IsNullOrEmpty(BackdropPath) ? "" : BackdropPath.StartsWith("http") ? BackdropPath : $"https://image.tmdb.org/t/p/w780{BackdropPath}";
        public string Year => string.IsNullOrEmpty(ReleaseDate) ? "" : ReleaseDate.Length >= 4 ? ReleaseDate.Substring(0, 4) : "";
        public string RatingDisplay => $"⭐ {VoteAverage:F1}";
        public string MediaTypeDisplay => MediaType == "tv" ? "📺 TV" : "🎬 Movie";
        public bool IsTv => MediaType == "tv";
    }
}

