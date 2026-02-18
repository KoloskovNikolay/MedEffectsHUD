MedEffectsHUD — Icons
=====================

This folder contains PNG icons displayed next to effect names in the HUD.
Enable icons in the BepInEx config: [Icons] Display Mode = IconAndText (or IconOnly)

FOLDER STRUCTURE:
  icons/              — Shared / fallback icons (used when no polarity-specific icon exists)
  icons/positive/     — Icons shown specifically for positive (buff) effects
  icons/negative/     — Icons shown specifically for negative (debuff) effects

  The mod searches: polarity folder first → then root icons/ folder as fallback.

INCLUDED ICONS — Root (73):
  Health Effects:
    Pain.png, PainKiller.png, Contusion.png, Tremor.png, Stun.png, Flash.png,
    LightBleeding.png, HeavyBleeding.png, Bleeding.png, Fracture.png,
    FreshWound.png, Wound.png, Surgery.png, Berserk.png, PanicAttack.png,
    MildMusclePain.png, SevereMusclePain.png, MusclePain.png,
    HandsTremor.png, "Hands tremor.png",
    TunnelVision.png, "Tunnel effect.png",
    Intoxication.png, LethalIntoxication.png,
    Exhaustion.png, Dehydration.png

  Environment:
    BodyTemperature.png, Frostbite.png, FrostbiteBuff.png, RadExposure.png

  Weight Effects:
    Encumbered.png, OverEncumbered.png, WeightLimit.png,
    Fatigue.png, ChronicStaminaFatigue.png

  Stim / Recovery Buffs:
    Regeneration.png, HealthRate.png, "Health regeneration.png",
    EnergyRate.png, "Energy recovery.png",
    HydrationRate.png, "Hydration recovery.png",
    StaminaRate.png, StaminaRegen.png, "Stamina recovery.png",
    Endurance.png, MaxStamina.png, "Max stamina.png",
    "Damage reduction.png", "Damage taken.png",
    Antidote.png, RemoveAllBloodLosses.png,
    "Stops and prevents bleedings.png",
    Health.png, Vitality.png, Strength.png,
    Perception.png, Attention.png

  Skill / Stat Buffs:
    Charisma.png, Intellect.png, Immunity.png, Metabolism.png,
    ImmunityPreventedNegativeEffect.png,
    HeavyArmor.png, LightArmor.png, HeavyVests.png, LightVests.png,
    MagazineLoading.png, MagDrills.png,
    StressResistance.png, "Stress Resistance.png",
    QuantumTunnelling.png

INCLUDED ICONS — Polarity-Specific:
  positive/ (9):
    "Damage taken (except the head).png", Endurance.png, EnergyRate.png,
    HealthRate.png, HydrationRate.png, RemoveAllBloodLosses.png,
    "Stamina recovery.png", Strength.png, StressResistance.png

  negative/ (11):
    "Damage taken (except the head).png", Endurance.png, EnergyRate.png,
    HandsTremor.png, Health.png, HealthRate.png, HydrationRate.png,
    StaminaRate.png, StaminaRegen.png, WeightLimit.png, Wound.png

FILE NAMING:
  Icon files are matched by internal EffectId first, then by display name.
  This means the same icon works regardless of game language.
  Check BepInEx log for "[Icons] Loaded 'XYZ'" messages to confirm loading.
  If an icon isn't loading, look for "[Icons] No icon found for effectId='...'"
  in the log — that tells you what filename the mod expects.

CUSTOM ICONS:
  You can add or replace any icon. Use 32×32 transparent PNG for best results.
  The file name must match the EffectId (case-insensitive).
  To use different icons for the same effect when it's positive vs negative,
  place them in the positive/ or negative/ subfolder.

INSTALLATION:
  Copy this entire folder (with subfolders) to:
    BepInEx/plugins/MedEffectsHUD/icons/
  Or if the DLL is inside BepInEx/plugins/MedEffectsHUD/:
    place icons/ next to the DLL.
