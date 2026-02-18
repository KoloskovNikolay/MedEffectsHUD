# MedEffectsHUD v1.1.3 — Patch Notes

**Release Date:** February 18, 2026

---

## Packaging

- Repacked the release archive to ensure Dragon Den accepts the folder layout
- Normalized archive paths to use forward slashes
- Top-level structure now starts at `BepInEx/`

## Installation

1. Extract `MedEffectsHUD-v1.1.3.zip` into the **game root folder** (where `EscapeFromTarkov.exe` is)
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
