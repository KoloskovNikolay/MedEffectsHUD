# MedEffectsHUD v1.1.1 — Patch Notes

**Release Date:** February 18, 2026

---

## Bug Fixes

- **Fixed HUD background not loading from config after map change.**
  Previously, the background alpha setting would visually reset to default when loading a new map,
  requiring manual re-selection each time. The root cause was that Unity destroyed the background
  texture during scene transitions while the mod skipped style re-creation because config values
  hadn't changed. Now GUI styles properly rebuild on map load, and textures are protected from
  Unity's scene cleanup.

## New Icons

- **Frostbite / FrostbiteBuff** — frostbite effect icon (32×32 PNG)
- **HandsTremor** — hands tremor effect icon (32×32 PNG)
- **Wound** — wound effect icon (32×32 PNG)

All new icons are available in both the root `icons/` folder (fallback) and `icons/negative/`
(polarity-specific) for proper display regardless of how the game classifies the effect.

## Icon Improvements

- Converted all `.webp` icons to 32×32 PNG format — `.webp` files removed from the mod
- Fixed icon filename `"Hands tremor.png"` → `HandsTremor.png` to match game's EffectId
- Fixed icon filename `"wound.png"` → `Wound.png` for consistency
- Fixed typo in icon filename `"Damage rediction.png"` → `"Damage reduction.png"`
- Updated `icons/README.txt` with complete file inventory (73 root + 9 positive + 11 negative icons),
  corrected config instructions, and folder structure documentation

## Installation

1. Extract `MedEffectsHUD-v1.1.1.zip` into the **game root folder** (where `EscapeFromTarkov.exe` is)
2. The archive mirrors the game folder structure:
   ```
   <Game Root>/
   └── BepInEx/
       └── plugins/
           ├── MedEffectsHUD.dll
           └── MedEffectsHUD/
               └── icons/
                   ├── *.png (shared icons)
                   ├── positive/ (buff-specific icons)
                   └── negative/ (debuff-specific icons)
   ```
3. Configure via BepInEx config manager (F12) → MedEffectsHUD
