# ⚡ Spoky's Project Lightning

> Game manifest manager, fix installer, and movie streaming app for Windows.

A WPF desktop app that helps you manage Steam game manifests and fixes, stream movies/TV shows, apply bypasses for various game launchers, and more — all with a slick neon-themed UI.

---

## Features

### 🎮 Game Management
- **Add Games** — Search Steam by name, App ID, or URL; browse recommended and trending games
- **Install Manifests** — Download and install Steam game manifests from multiple sources
- **Manage Library** — View installed games, play them, open install folders, remove games
- **Drag & Drop** — Drop `.lua`, `.manifest`, `.zip`, `.rar`, `.7z`, `.001` files to install them instantly

### 🛡️ Fixes & Bypasses
- **Fixes** — Browse and install game manifests + fixes from community sources (Ryuu, Ryujin)
- **Bypasses** — Apply bypasses for Ubisoft, EA, Rockstar, Denuvo, PlayStation games
- **Online Fixes** — Browse, download, and install online fixes from Online-Fix.me
- **Installation Queue** — Real-time progress tracking with overall completion status

### 🎬 Movies & TV Shows
- **Streaming** — Built-in ad-free player with WebView2
- **Search** — Powered by TMDB (primary) with OMDb fallback and IMDb→TMDB conversion
- **Categories** — Trending, Popular, Top Rated, Now Playing, Upcoming, On TV
- **Multi-Source** — Auto-fallback between streaming sources
- **Fullscreen** — Immersive true fullscreen with auto-hiding sidebar and title bar

### 🎨 Customization
- **Themes** — Dark, Light, Emerald, Midnight Blue, Royal Purple
- **Custom Colors** — Override any color group with hex values
- **Video Background** — Drop MP4 files or pick from bundled wallpapers (including dragon animations)
- **Auto Palettes** — Extracts theme colors from background videos

### 🛠️ Tools
- **In-App Browser** — WebView2-based tool panel with navigation controls
- **Lightning Tools** — Install/uninstall the Steam backend required to play games

### 🔄 Updates
- **Auto-Update** — Checks for new versions on startup with download progress and one-click install
- **Custom Update URL** — Configure your own update manifest endpoint from Settings

### ⚙️ Settings
- Steam installation path (auto-detect or browse)
- Download folder configuration
- Theme and color customization
- Video background management
- Update URL configuration
- One-click install/uninstall of Lightning Tools

---

## Tech Stack

- **Framework**: .NET 8 WPF (Windows-only)
- **UI**: Custom neon/dark theme with Material-inspired cards, drop shadows, and glow effects
- **Browser**: Microsoft WebView2 (embedded Chromium)
- **Data**: SQLite, JSON
- **Libraries**: HtmlAgilityPack, Newtonsoft.Json, SharpCompress, System.Data.SQLite

---

## Pages

| Page | Description |
|------|-------------|
| **Home** | Featured content, most downloaded, quick links, drag-drop install zone |
| **Add** | Search Steam games, browse recommendations/new releases, add by App ID |
| **Manage** | View and manage installed games with search and pagination |
| **Fixes** | Browse game fixes with Discord auth, search, pagination |
| **Movies** | Stream movies/TV shows with search, categories, and fullscreen player |
| **Tools** | In-app web browser for various game-related tools |
| **Settings** | All configuration options |
| **Online Fixes** | Download online fixes with progress tracking |
| **Bypasses** | Apply community bypasses filtered by platform |
| **Library** | Steam game manifests from third-party sources |

---

## Installation

Download the latest installer from [Releases](https://github.com/spokyishuman/Spoky-s-Project-Vercel/releases).

**System Requirements:**
- Windows 10/11 64-bit
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (usually pre-installed on Windows 11)
- Steam client installed

---

## Development

Built with Visual Studio / .NET 8 SDK.

```bash
git clone https://github.com/spokyishuman/Spoky-s-Project-Vercel.git
cd SpokysProjectLightning
dotnet build
```

---

## Project Structure

```
SpokysPL/
├── Views/           # XAML pages (Home, Add, Manage, Fixes, Movies, etc.)
├── ViewModels/      # MVVM view models
├── Services/        # Data, download, theme, update, navigation, scraping
├── Models/          # Data models
├── Themes/          # XAML theme dictionaries (Dark, Light, Emerald, etc.)
├── Converters/      # XAML value converters
└── Resources/       # App resources
```

---

*Built with 💀 by Spoky*
