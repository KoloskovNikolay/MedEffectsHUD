MedEffectsHUD — Icons
=====================

This folder contains PNG icons displayed next to effect names in the HUD.
Enable icons in the BepInEx config: [Icons] Enabled = true

INCLUDED ICONS (45):
  Health Effects:
    Pain.png, PainKiller.png, Contusion.png, Tremor.png, Stun.png, Flash.png,
    LightBleeding.png, HeavyBleeding.png, Bleeding.png, Fracture.png,
    FreshWound.png, Surgery.png, Berserk.png, PanicAttack.png,
    MildMusclePain.png, SevereMusclePain.png, MusclePain.png,
    TunnelVision.png, Intoxication.png, LethalIntoxication.png,
    Exhaustion.png, Dehydration.png, RadExposure.png

  Weight Effects:
    Encumbered.png, OverEncumbered.png, WeightLimit.png,
    Fatigue.png, ChronicStaminaFatigue.png

  Stim Buffs (generic icons):
    Regeneration.png, HealthRegen.png, EnergyRegen.png, HydrationRegen.png,
    StaminaRegen.png, Endurance.png, MaxStamina.png, DamageReduction.png,
    Antidote.png

  Skill Buffs:
    Charisma.png, Intellect.png, Health.png, Immunity.png, Metabolism.png,
    HeavyArmor.png, LightArmor.png, MagazineLoading.png

FILE NAMING:
  Icon files are matched by internal EffectId (NOT the localized name).
  This means the same icon works regardless of game language.
  Check BepInEx log for "[Icons] Loaded icon for 'XYZ'" messages to confirm.
  If an icon isn't loading, look for the EffectId in the log output.

CUSTOM ICONS:
  You can add or replace any icon. Use 32x32 transparent PNG for best results.
  The file name must match the EffectId exactly (case-insensitive).

INSTALLATION:
  Copy this folder to: BepInEx/plugins/MedEffectsHUD/icons/
