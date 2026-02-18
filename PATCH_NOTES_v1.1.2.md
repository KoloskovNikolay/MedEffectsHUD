# MedEffectsHUD v1.1.2 — Patch Notes

**Release Date:** February 18, 2026

---

## Packaging

- Rebuilt the release archive from scratch
- Cleaned up build and template folders from the repo
- No gameplay or feature changes in this release

## Installation

1. Extract `MedEffectsHUD-v1.1.2.zip` into the **game root folder** (where `EscapeFromTarkov.exe` is)
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
3. Configure via BepInEx config manager (F12) -> MedEffectsHUD
