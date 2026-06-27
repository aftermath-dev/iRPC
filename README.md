# iRPC

A lightweight Windows system tray app that shows your live iRacing session as Discord Rich Presence. Track, car, position, lap progress, fuel, weather, iRating, and more - all fully customizable.

![Discord Rich Presence preview](ArtAssets/preview.png)

---

## Features

- **Live session info in Discord** - track, car, session type, position, laps, time remaining, speed, fuel, last/best lap, weather, flags, pit and garage status
- **iRating and Strength of Field** - your live iRating, rolling averages (last 5 / 10 / custom races), and SoF using iRacing's own formula
- **Pit and damage tracking** - repair time remaining, fast repairs used/available, incident count
- **Per-session templates** - Practice, Qualify, Race, Test Drive, and Time Trial each get their own layout
- **Chip template editor** - click chips to build your presence, grouped by category with tooltips on each one. Classic text editor also available if you prefer it
- **Live preview in Settings** before saving
- **Track and car brand logos** pulled directly from GitHub - no Discord asset limit
- **Elapsed session timer** shown as a Discord timestamp
- **Stats window** - tracks your total time on track broken down by session type, car, and track
- **Pause Presence** - hide your presence from the tray menu without closing the app
- **Launches on Windows startup** (optional)
- **Self-updating** - checks GitHub Releases, downloads, verifies, and installs with one click

---

## Requirements

- Windows 10 or 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) - if missing, Windows will prompt you to install it on first launch
- Discord (desktop app)
- iRacing

---

## Installation

1. Download `iRPC.exe` from the [latest release](https://github.com/aftermath-dev/iRPC/releases/latest)
2. Run it - a tray icon appears in the system tray
3. Open iRacing and start a session

No installer required.

---

## Tray Menu

Right-click the tray icon for:

| Item              | Description                                          |
|-------------------|------------------------------------------------------|
| Settings          | Opens the Settings window                            |
| Stats             | Opens the time-on-track Stats window                 |
| Reconnect Discord | Forces a fresh connection to the Discord client      |
| Pause Presence    | Temporarily hides your presence without closing iRPC |
| Check for Updates | Manually checks GitHub for a newer release           |
| Exit              | Closes iRPC and clears your Discord presence         |

![Tray menu](ArtAssets/preview_tray.png)

---

## Settings

Right-click the tray icon - **Settings**

### Presence

Configure what appears on each line of the Discord presence. Pick a session type from the dropdown to customize each one independently.

**Details** and **State** are built using chips. Click a chip to add it to your template, drag to reorder, click × to remove. Hover any chip to see what it outputs. Chips are grouped by category (Session, Position, Lap Times, iRating, Speed & Fuel, Weather, Pit & Status). A classic text editor is also available under App settings if you prefer typing.

![Presence editor](ArtAssets/preview_settings_chipeditor.png)

Available chips:

| Chip                 | Output                                             |
|----------------------|----------------------------------------------------|
| Session              | Session type (Race, Practice...)                   |
| Track                | Track name                                         |
| Config               | Track configuration (Full, Boot...)                |
| Car                  | Car name                                           |
| Position             | P1, P2... (empty outside race)                     |
| Class Pos            | Position within your car class (multiclass races)  |
| Lap                  | Current lap number                                 |
| Lap Total            | Lap X/Y progress                                   |
| Laps Left            | Laps remaining                                     |
| Time                 | Time remaining                                     |
| Last Lap             | Last lap time                                      |
| Best Lap             | Best lap time this session                         |
| SoF                  | Strength of Field                                  |
| iRating              | Your live iRating                                  |
| iRating Avg 5        | Average of your last 5 race-end iRatings           |
| iRating Avg 10       | Average of your last 10 race-end iRatings          |
| iRating Avg (Custom) | Average over a custom window (set in App settings) |
| Sky                  | Current sky condition                              |
| Air °C / Air °F      | Air temperature                                    |
| Track °C / Track °F  | Track surface temperature                          |
| Pit Service          | Currently being serviced in the pits               |
| Pit Repair           | Mandatory damage repair time remaining             |
| Pit Opt Repair       | Optional/cosmetic repair time remaining            |
| Fast Repairs         | Fast repairs used/available this race              |
| Incidents            | Incident points this session                       |
| km/h / mph           | Speed                                              |
| Fuel / Fuel %        | Fuel level / percentage                            |
| Flag                 | Caution or Checkered (empty otherwise)             |
| Pit                  | In Pits indicator                                  |
| Garage               | In Garage indicator                                |

### Icons

| Setting          | Description                                  |
|------------------|----------------------------------------------|
| Large icon       | iRacing logo, iRPC logo, or track logo       |
| Large image text | Text shown when hovering the large icon (built with chips) |
| Small icon       | Off, car brand logo, or session type icon                  |
| Small image text | Text shown when hovering the small icon (built with chips) |

![Icons settings](ArtAssets/preview_settings_icons.png)

### App

| Setting                      | Description                                                |
|------------------------------|------------------------------------------------------------|
| Discord App ID               | Discord application used for Rich Presence                 |
| Show GitHub button           | Show a link to this repo on the presence                   |
| Launch on startup            | Start iRPC automatically with Windows                      |
| Check for updates on startup | Silently check for a newer release each time iRPC launches |
| Auto-populate key overrides  | Append newly seen tracks/cars to `key_overrides.json`      |
| iRating average window       | Number of races used for the "Avg (Custom)" iRating brick  |
| Debug mode                   | Enables verbose file logging to `iRPC.log`                 |

---

## Stats

Open via the tray menu - **Stats**. Shows your accumulated time on track, broken down by session type, car, and track. Saved locally, no account needed.

![Stats window](ArtAssets/preview_stats.png)

---

## Track & Brand Logos

Logos are served directly from `ArtAssets/Tracks/` and `ArtAssets/Brands/` in this repo - no Discord asset upload required. To add a logo, add a PNG with the right filename and push.

**Filename convention:** lowercase, spaces and hyphens replaced with underscores, prefixed with `track_` or `brand_`.

| Type         | Example  | Filename            |
|--------------|----------|---------------------|
| Track        | Spa      | `track_spa.png`     |
| Car brand    | Ferrari  | `brand_ferrari.png` |
| Session icon | Practice | `icon_practice.png` |

iRPC generates the asset key automatically from the track/car name reported by iRacing. If it doesn't match your filename, add a remap in `%AppData%\iRPC\key_overrides.json`:

```json
{
  "track_spa_francorchamps": "track_spa"
}
```

Common remaps are pre-seeded. Enable Debug Mode to see exactly which key and URL the app is resolving each session.

---

## Building from Source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8).

```powershell
git clone https://github.com/aftermath-dev/iRPC.git
cd iRPC
dotnet run
```

To publish a release build:

```powershell
dotnet publish -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -p:AssemblyName=iRPC
```

Output: `bin/Release/net8.0-windows/win-x64/publish/iRPC.exe` (~1 MB, requires .NET 8 runtime)

---

## Logs & Data Files

| File                                  | Contents                                                            |
|---------------------------------------|---------------------------------------------------------------------|
| `%AppData%\iRPC\iRPC.log`             | Debug log - poll ticks, YAML updates, resolved image URLs           |
| `%AppData%\iRPC\settings.json`        | App settings                                                        |
| `%AppData%\iRPC\stats.json`           | Time-on-track stats (session type / car / track)                    |
| `%AppData%\iRPC\irating_history.json` | Your recorded race-end iRatings, used for rolling averages          |
| `%AppData%\iRPC\key_overrides.json`   | Asset key remaps (e.g. `track_spa_francorchamps` -> `track_spa`)    |
| `%AppData%\iRPC\tracks.txt`           | Every unique track seen, auto-appended per session (if enabled)     |
