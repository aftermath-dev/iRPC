# iRPC

A lightweight Windows system tray app that displays your iRacing session as Discord Rich Presence — live track, car, position, lap progress, fuel, speed, and more.

![Discord Rich Presence preview](ArtAssets/preview.png)

---

## Features

- Live session info in Discord: track, car, session type, position, laps, time, speed, fuel, flags, pit/garage status
- Per-session-type presence customization (Practice, Qualify, Race, Test Drive, Time Trial)
- Drag-and-drop brick editor — build your Details and State lines visually, no typing required
- Live preview in Settings before you save
- Track and car brand logos served directly from GitHub — no Discord asset uploads, no 300-asset limit
- Elapsed session timer as a Discord timestamp
- Caution and checkered flag indicators
- Launches on Windows startup (optional)
- Automatic update checks via GitHub Releases

---

## Requirements

- Windows 10 or 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) — if missing, Windows will prompt you to install it on the first launch
- Discord (desktop app)
- iRacing

---

## Installation

1. Download `iRPC.exe` from the [latest release](https://github.com/aftermath-dev/iRPC/releases/latest)
2. Run it — a tray icon appears in the system tray
3. Open iRacing and start a session

No installer required.

---

## Settings

Right-click the tray icon → **Settings**

### Presence

Configure what appears on each line of the Discord presence. Select a session type from the dropdown to customize each one independently — changes to **Default** apply as the fallback for any session type not explicitly configured.

**Details** and **State** are built using draggable bricks. Click a brick in the *Available* pool to add it, drag active bricks to reorder, click × to remove. Hover any brick for a description of what it outputs.

Available bricks:

| Brick     | Output                                 |
|-----------|----------------------------------------|
| Session   | Session type (Race, Practice...)       |
| Track     | Track name                             |
| Config    | Track configuration (Full, Boot...)    |
| Car       | Car name                               |
| Position  | P1, P2... (empty outside race)         |
| Lap       | Current lap number                     |
| Lap Total | Lap X/Y progress                       |
| Laps Left | Laps remaining                         |
| Time      | Time remaining                         |
| km/h      | Speed in km/h                          |
| mph       | Speed in mph                           |
| Fuel      | Fuel in litres                         |
| Fuel %    | Fuel percentage                        |
| Flag      | Caution or Checkered (empty otherwise) |
| Pit       | In Pits indicator                      |
| Garage    | In Garage indicator                    |

### Icons

| Setting          | Description                                  |
|------------------|----------------------------------------------|
| Large icon       | iRacing logo, iRPC logo, or track logo       |
| Large image text | Bricks shown as hover text on the large icon |
| Small icon       | Off, car brand logo, or session type icon    |
| Small image text | Bricks shown as hover text on the small icon |

### App

| Setting                     | Description                                         |
|-----------------------------|-----------------------------------------------------|
| Discord App ID              | Discord application used for Rich Presence          |
| Show elapsed timer          | Display elapsed session time as a Discord timestamp |
| Show GitHub button          | Show a link to this repo on the presence            |
| Launch on startup           | Start iRPC automatically with Windows               |
| Auto-populate key overrides | Append newly seen tracks to `key_overrides.json`    |

---

## Track & Brand Logos

Logos are served directly from `ArtAssets/Tracks/` and `ArtAssets/Brands/` in this repo — no Discord asset upload required. To add a new logo, add a PNG with the right filename and push.

**Filename convention:** lowercase, spaces and hyphens replaced with underscores, prefixed with `track_` or `brand_`.

| Type         | Example  | Filename            |
|--------------|----------|---------------------|
| Track        | Spa      | `track_spa.png`     |
| Car brand    | Ferrari  | `brand_ferrari.png` |
| Session icon | Practice | `icon_practice.png` |

iRPC auto-generates the key from the track/car name reported by iRacing. If the generated key doesn't match your filename (e.g., iRacing calls it "Spa-Francorchamps" → `track_spa_francorchamps`), add a remap in `%AppData%\iRPC\key_overrides.json`:

```json
{
  "track_spa_francorchamps": "track_spa"
}
```

Common remaps are pre-seeded. Check `%AppData%\iRPC\iRPC.log` to see exactly which key and URL the app is resolving for each session.

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
dotnet publish -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -p:AssemblyName=iRPC-v0.0.5
```

Output: `bin/Release/net8.0-windows/win-x64/publish/iRPC-v0.0.5.exe` (~1 MB)

---

## Logs & Data Files

| File                                | Contents                                                            |
|-------------------------------------|---------------------------------------------------------------------|
| `%AppData%\iRPC\iRPC.log`           | Debug log — poll ticks, YAML updates, resolved image URLs           |
| `%AppData%\iRPC\tracks.txt`         | Every unique track seen, auto-appended on each new session          |
| `%AppData%\iRPC\key_overrides.json` | Asset key remappings (e.g. `track_spa_francorchamps` → `track_spa`) |
| `%AppData%\iRPC\settings.json`      | App settings                                                        |
