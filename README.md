# MedEffectsHUD

**In-raid HUD overlay for SPT (Single Player Tarkov) that displays active buffs and debuffs.**

Works with stimulators, medical items, skill effects, bleedings, fractures, and every other health effect the game tracks.

![MedEffectsHUD screenshot](https://i.postimg.cc/6qgGztFY/Screenshot.png)

---

## Features

### Display Modes
- **TextOnly** — classic two-column text layout (positive green / negative red)
- **IconAndText** — icons alongside effect names and timers
- **IconOnly** — compact icon grid with timers below each icon

### Icon System
- **70+ built-in icons** (32×32 PNG) covering all health effects, buffs, and skills
- **Polarity-aware icons** — separate `positive/` and `negative/` icon folders for effects that look different depending on polarity (e.g. green vs red health regen)
- **Locale-independent** — icons are matched by internal effect ID, not by display language
- **Customizable icon size** — configurable in settings

### Layout Options
- **Stacked** — positive effects on top, negative below (with separator)
- **Side-by-side** — positive left column, negative right column
- **Manual HUD height** — set a fixed height or leave at 0 for auto-calculation
- **Hide empty columns** — HUD disappears when no effects are active

### Notifications
- **Center-screen popups** — visual alerts when a buff expires or a new debuff is received
- **Configurable position and icon size** for notifications

### Text & Font
- **Custom font selection** — dropdown with all available system fonts
- **Use Game Font** — toggle to use the game's native Bender font
- **Configurable font size** — separate from icon size

### Core Features
- **Auto-discovery** — finds all active buffs/debuffs via multiple fallback strategies (event subscription, deep reflection scan, `GetAllEffects`, `ActiveBuffsNames`)
- **Remaining time** — shows countdown for every timed effect
- **Blink warning** — effects about to expire blink on screen
- **Name abbreviation** — long buff names are shortened automatically
- **Sort by time** — effects expiring soonest appear at the top
- **Fully configurable** — position, size, font, colours, display options — everything is in BepInEx config (press **F12** in-game with [ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager))

---

## Installation

1. Download the latest release archive (`MedEffectsHUD-v1.1.3.zip`).
2. Extract it into your **SPT root folder** (the one with `EscapeFromTarkov.exe`).
   - The DLL goes to `BepInEx/plugins/MedEffectsHUD.dll`
   - Icons go to `BepInEx/plugins/MedEffectsHUD/icons/`
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
You can also edit them live with **ConfigurationManager** (F12).

### Config sections

| Section | Key | Default | Description |
|---|---|---|---|
| **General** | Toggle Key | F8 | Show / hide the HUD |
| **Position** | X | 10 | HUD X position (px from left) |
| | Y | 200 | HUD Y position (px from top) |
| | Width | 560 | HUD panel width |
| | Height | 0 | Fixed HUD height (0 = auto) |
| **Appearance** | Font Size | 13 | Text size |
| | Font Name | Arial | Font family |
| | Use Game Font | true | Use the game's Bender font |
| | Background Alpha | 0.85 | Panel opacity (0–1) |
| **Icons** | Display Mode | IconAndText | `TextOnly` / `IconAndText` / `IconOnly` |
| | Size | 32 | Icon size in pixels |
| **Notifications** | Icon Size | 48 | Center-screen notification icon size |
| | Position Y | 0.35 | Vertical position (0=top, 1=bottom) |
| **Display** | Time Format | Auto | `Auto` / `SecondsOnly` / `MinutesOnly` |
| | Show Values | true | Show numeric values like (+30) |
| | Show Time | true | Show remaining time |
| **Layout** | Effect Layout | SideBySide | `Stacked` / `SideBySide` |
| | Line Spacing | 2 | Extra space between lines |
| | Hide Empty Columns | true | Hide column if no effects |
| | Abbreviate Names | true | Shorten long buff names |
| | Sort By Time | true | Sort by remaining time |
| **Blink** | Enabled | true | Blink expiring effects |
| | Threshold Seconds | 10 | Start blinking threshold |
| | Speed | 3 | Blink frequency (Hz) |

### Custom Icons

You can add or replace icons by placing 32×32 PNG files in:
- `BepInEx/plugins/MedEffectsHUD/icons/` — default icons (used as fallback)
- `BepInEx/plugins/MedEffectsHUD/icons/positive/` — icons for positive effects
- `BepInEx/plugins/MedEffectsHUD/icons/negative/` — icons for negative effects

Icon filenames should match the effect's internal ID (e.g. `PainKiller.png`, `Fracture.png`) or display name (e.g. `Health regeneration.png`).

---

## Building from source

### Prerequisites

- .NET SDK (any version that supports `net472` target)
- A working SPT 4.0.x installation (the project references game DLLs)

### Steps

```bash
cd MedEffectsHUD
dotnet build -c Release
```

The compiled DLL appears in `MedEffectsHUD/bin/Release/MedEffectsHUD.dll`.

> The `.csproj` expects the SPT installation at `E:\STP4.0.10`. Edit the `<GameDir>` property if your path differs.

---

## License

This mod is released under the [MIT License](LICENSE).

---

## Credits

- **koloskovnick** — author
- **BepInEx** — modding framework
- **SPT Team** — Single Player Tarkov
