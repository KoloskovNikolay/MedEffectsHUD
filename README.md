# MedEffectsHUD

**In-raid HUD overlay for SPT (Single Player Tarkov) that displays active buffs and debuffs.**

Works with stimulators, medical items, skill effects, bleedings, fractures, and every other health effect the game tracks.

![MedEffectsHUD screenshot](https://i.imgur.com/PLACEHOLDER.png)
<!-- Replace the link above with an actual screenshot before publishing -->

---

## Features

- **Two-column layout** — positive (green) and negative (red) effects side by side
- **Auto-discovery** — finds all active buffs/debuffs via multiple fallback strategies (event subscription, deep reflection scan, `GetAllEffects`, `ActiveBuffsNames`)
- **Remaining time** — shows countdown for every timed effect
- **Blink warning** — effects about to expire blink on screen
- **Name abbreviation** — long buff names are shortened automatically (configurable)
- **Sort by time** — effects expiring soonest appear at the top
- **Hide empty columns** — columns with no effects are hidden; if nothing is active the HUD is invisible
- **Fully configurable** — position, size, font, colours, display options — everything is in BepInEx config (press **F5** in-game with [ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager))

---

## Installation

1. Download the latest release archive (`.zip` or `.7z`).
2. Extract it into your **SPT root folder** (the one with `EscapeFromTarkov.exe`).
   - The DLL goes to `BepInEx/plugins/MedEffectsHUD.dll`.
3. Launch the game. Press **F8** (default) to toggle the HUD.

### Requirements

| Dependency | Version |
|---|---|
| **SPT** | 4.0.x |
| **BepInEx 5** | included with SPT |

No server-side mod is required.

---

## Configuration

All settings are in `BepInEx/config/com.koloskovnick.medeffectshud.cfg` (generated on first run).  
You can also edit them live with **ConfigurationManager** (F5).

### Config sections

| Section | Key | Default | Description |
|---|---|---|---|
| **General** | Toggle Key | F8 | Show / hide the HUD |
| **Position** | X | 10 | HUD X position (px from left) |
| | Y | 200 | HUD Y position (px from top) |
| | Width | 560 | HUD panel width |
| **Appearance** | Font Size | 13 | Text size |
| | Background Alpha | 0.85 | Panel opacity (0 = transparent, 1 = solid) |
| **Display** | Time Format | Auto | `Auto` / `SecondsOnly` / `MinutesOnly` |
| | Show Values | true | Show numeric values like (+30) next to names |
| | Show Time | true | Show remaining time |
| **Layout** | Line Spacing | 2 | Extra space (px) between lines |
| | Show Title | false | Show "PLAYER BUFFS" header |
| | Show Column Headers | false | Show "POSITIVE / NEGATIVE" labels |
| | Hide Empty Columns | true | Hide column if it has no effects |
| | Abbreviate Names | true | Shorten long buff names |
| | Sort By Time | true | Sort effects by remaining time (ascending) |
| **Blink** | Enabled | true | Blink effects about to expire |
| | Threshold Seconds | 10 | Start blinking when ≤ this many seconds remain |
| | Speed | 3 | Blink frequency (Hz) |

---

## Building from source

### Prerequisites

- .NET SDK (any version that supports `net472` target)
- A working SPT 4.0.x installation (the project references game DLLs)

### Steps

```bash
cd MedEffectsHUD
dotnet build -c Debug
```

The compiled DLL appears in `MedEffectsHUD/bin/Debug/MedEffectsHUD.dll`.

> The `.csproj` expects the SPT installation at `E:\STP4.0.10`. Edit the `<GameDir>` property if your path differs.

---

## License

This mod is released under the [MIT License](LICENSE).

---

## Credits

- **koloskovnick** — author
- **BepInEx** — modding framework
- **SPT Team** — Single Player Tarkov
