# MedEffectsHUD — Copilot Instructions

## Project Overview

**MedEffectsHUD** is a BepInEx client-side plugin for SPT (Single Player Tarkov) that displays active buffs and debuffs in a configurable HUD overlay during raids.

- **Version:** 1.1.3
- **Platform:** SPT 4.0.10, Unity 2022.3.43f1
- **Framework:** BepInEx 5 (included with SPT)
- **Target:** .NET Framework 4.7.2

---

## Architecture

### Plugin Structure

```
MedEffectsHUDPlugin : BaseUnityPlugin
│
├── Lifecycle Methods:
│   ├── Awake()           — Config initialization, icons folder setup
│   ├── Update()          — Toggle key, player ref update, periodic refresh
│   └── OnGUI()           — Render HUD, notifications
│
├── Discovery Strategies (Multi-Layered):
│   ├── Strategy 1: Event Subscription
│   │   └── SubscribeEvents() → OnBuffAdded/Removed
│   ├── Strategy 2: Deep Recursive Scan
│   │   └── DeepScanHealthController() → finds IPlayerBuff objects
│   ├── Strategy 3: Container Tracking
│   │   └── TryReadBuffContainer() → GClass3056 containers with TimeLeft
│   └── Strategy 4: GetAllEffects() Direct API
│       └── ReadHealthEffects() → IEffect enumeration
│
├── Data Structures:
│   ├── _capturedBuffs: Dictionary<string, object>
│   │   └── Key format: "{BuffName}|{EffectId}|{BodyPart}"
│   ├── _buffToContainer: Dictionary<int, object>
│   │   └── Maps buff identity hash → parent container
│   ├── _buffWholeTimeOffset: Dictionary<int, float>
│   │   └── Tracks WholeTime at (re)application for timer reset detection
│   └── _positiveEffects / _negativeEffects: List<DisplayEffect>
│
├── Display Modes:
│   ├── TextOnly          — Classic two-column text
│   ├── IconAndText       — Icons + text labels
│   └── IconOnly          — Compact icon grid
│
├── Icon System:
│   ├── _iconCache: Dictionary<string, Texture2D>
│   ├── Icon resolution order:
│   │   1. icons/{polarity}/{EffectId}.png
│   │   2. icons/{polarity}/{DisplayName}.png
│   │   3. icons/{EffectId}.png
│   │   4. icons/{DisplayName}.png
│   └── 70+ built-in icons (32×32 PNG)
│
└── Notifications:
    ├── Center-screen popups for:
    │   ├── Buff expiring (< threshold)
    │   └── New debuff received
    └── Configurable icon size & position
```

---

## EFT/SPT Core Classes Used

### Health System (EFT.HealthSystem)

```csharp
// Primary interface
interface IHealthController
  Methods:
    GetAllEffects() → IEnumerable<IEffect>
    ActiveBuffsNames() → IEnumerable<string>  // Diagnostic only
  Events (discovered via reflection):
    Action<IPlayerBuff> OnBuffAdded
    Action<IPlayerBuff> OnBuffRemoved

// Effect interface
interface IEffect
  Properties:
    EBodyPart BodyPart
    // Contains nested buff data in some implementations

// Buff interface (resolved at runtime via reflection)
interface IPlayerBuff
  Properties:
    string BuffName          // Display name with Unity rich-text tags
    string EffectId          // Internal ID (e.g. "PainKiller", "Fracture")
    EBodyPart BodyPart       // Head, Chest, Stomach, LeftArm, etc.
    float Value              // Buff strength (positive or negative)
    float WholeTime          // Total elapsed time since FIRST application
    bool Active              // If false, buff is expired
    object Settings          // Contains Duration property
  Nested:
    Settings.Duration → float  // Original buff duration

// Buff container (obfuscated class GClass3056)
// Discovered via deep scan, has:
  List<IPlayerBuff> Buffs
  float TimeLeft             // Shared timer for all buffs in container
```

### Player & Game World (EFT)

```csharp
class Player : IPlayer
  Properties:
    IHealthController HealthController
    bool IsYourPlayer              // Identifies local player

class GameWorld : MonoBehaviour
  Properties:
    Dictionary<string, Player> allAlivePlayersByID

// Singleton access
Singleton<GameWorld>.Instance
Singleton<GameWorld>.Instantiated   // Check before accessing
```

### Comfort.Common

```csharp
namespace Comfort.Common
  class Singleton<T> where T : class
    static T Instance
    static bool Instantiated
```

---

## Key Patterns & Best Practices

### 1. Runtime Type Resolution (Obfuscation Safe)

EFT types are obfuscated. Always resolve via reflection:

```csharp
private Type _ipbType;
private void ResolveIPB()
{
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
        if (!asm.FullName.Contains("Assembly-CSharp")) continue;
        foreach (var t in asm.GetTypes())
        {
            if (t.Name == "IPlayerBuff" && t.IsInterface)
            {
                _ipbType = t;
                return;
            }
        }
    }
}
```

### 2. Event Subscription with Dynamic Types

```csharp
// Build Action<IPlayerBuff> delegate dynamically
var actionType = typeof(Action<>).MakeGenericType(_ipbType);

// Create wrapper to bridge dynamic type to our method
class BuffEventWrapper
{
    private MedEffectsHUDPlugin _plugin;
    public void Handle(object buff) => _plugin.OnBuffAddedObj(buff);
}

var wrapper = new BuffEventWrapper(this, isRemove);
var handler = Delegate.CreateDelegate(actionType, wrapper, 
    typeof(BuffEventWrapper).GetMethod("Handle"));

// Combine with existing event handler
var existing = (Delegate)field.GetValue(healthController);
var combined = Delegate.Combine(existing, handler);
field.SetValue(healthController, combined);
```

### 3. Buff Timer Management (Critical for Reapplication)

Buffs can be **reapplied** (e.g., using another stim). The game reuses the same buff object, so:

- `WholeTime` keeps incrementing from original application
- `Duration` stays constant
- Container's `TimeLeft` is reset to full duration

**Solution:** Record `WholeTime` offset at (re)application:

```csharp
// On buff added/reapplied:
int bid = RuntimeHelpers.GetHashCode(buff);
float currentWholeTime = GetFloatProp(buff, "WholeTime");
_buffWholeTimeOffset[bid] = currentWholeTime;

// When reading time:
float GetBuffTimeLeft(object buff)
{
    float duration = GetSettingsDuration(buff);
    float elapsed = GetFloatProp(buff, "WholeTime");
    float offset = _buffWholeTimeOffset.TryGetValue(bid, out var off) ? off : 0f;
    float adjustedElapsed = elapsed - offset;
    float remaining = duration - adjustedElapsed;
    
    // Also check container's TimeLeft (can be higher after reapplication)
    float containerTL = GetContainerTimeLeft(buff);
    return Math.Max(remaining, containerTL);  // Take the MAXIMUM
}
```

### 4. Reflection Helpers

```csharp
// Safe property getter with fallback
private float GetFloatProp(object obj, string propName, float fallback = -1f)
{
    try
    {
        var prop = obj.GetType().GetProperty(propName, 
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop == null) return fallback;
        var val = prop.GetValue(obj);
        return val is float f ? f : fallback;
    }
    catch { return fallback; }
}

// Same pattern for string, bool, etc.
```

### 5. GUI Style Initialization (OnGUI)

Unity IMGUI styles must be initialized in `OnGUI()`, not in `Awake()`:

```csharp
private void OnGUI()
{
    if (!_stylesInit)
    {
        InitStyles();
        _stylesInit = true;
    }
    // ... render HUD
}

private void InitStyles()
{
    _boxStyle = new GUIStyle(GUI.skin.box);
    _posStyle = new GUIStyle(GUI.skin.label) { richText = true };
    // ... configure colors, fonts, etc.
}
```

**Important:** Reset `_stylesInit = false` on map change to handle scene reloads.

### 6. Icon Loading & Caching

```csharp
private Texture2D LoadIcon(string effectId, bool isPositive)
{
    string cacheKey = $"{effectId}|{(isPositive ? "P" : "N")}";
    if (_iconCache.TryGetValue(cacheKey, out var cached)) return cached;
    if (_iconMissing.Contains(cacheKey)) return null;

    // Try polarity-specific folder first
    string polarityFolder = isPositive ? _iconsPositiveFolder : _iconsNegativeFolder;
    string path = Path.Combine(polarityFolder, $"{effectId}.png");
    
    if (!File.Exists(path))
        path = Path.Combine(_iconsFolder, $"{effectId}.png");
    
    if (!File.Exists(path))
    {
        _iconMissing.Add(cacheKey);
        return null;
    }

    var tex = new Texture2D(2, 2);
    tex.LoadImage(File.ReadAllBytes(path));
    _iconCache[cacheKey] = tex;
    return tex;
}
```

### 7. Deep Scan Strategy (Fallback Discovery)

```csharp
private void DeepScanHealthController()
{
    var visited = new HashSet<int>();  // Track by identity hash
    DeepScan(_healthController, depth: 0, maxDepth: 5, visited, "HC");
}

private int DeepScan(object obj, int depth, int maxDepth, 
                     HashSet<int> visited, string path)
{
    if (obj == null || depth > maxDepth) return 0;
    
    int id = RuntimeHelpers.GetHashCode(obj);
    if (visited.Contains(id)) return 0;
    visited.Add(id);

    // Check if this object IS a buff
    if (IsIPB(obj)) return CaptureBuffFromScan(obj, path);

    // Check if this object is a buff CONTAINER
    int found = TryReadBuffContainer(obj, path);

    // Recurse into fields
    foreach (var field in obj.GetType().GetFields(BindingFlags))
    {
        var val = field.GetValue(obj);
        if (val is IList list)
            foreach (var item in list)
                found += DeepScan(item, depth + 1, maxDepth, visited, path + "." + field.Name);
        else if (val != null)
            found += DeepScan(val, depth + 1, maxDepth, visited, path + "." + field.Name);
    }
    return found;
}
```

---

## Configuration System (BepInEx)

### Config Entry Pattern

```csharp
private ConfigEntry<T> _configEntry;

_configEntry = Config.Bind(
    "Section Name",
    "Key Name",
    defaultValue,
    new ConfigDescription(
        "Description shown in F12 menu",
        new AcceptableValueRange<float>(min, max)  // Optional constraint
    )
);

// Add callback for live changes
_configEntry.SettingChanged += (sender, args) => OnConfigChanged();

// Access value
var value = _configEntry.Value;
```

### Custom Types in Config

```csharp
public enum TimeFormat { Auto, SecondsOnly, MinutesOnly }

_timeFormat = Config.Bind("Display", "Time Format", TimeFormat.Auto, 
    "Auto = Xm XXs when ≥60s else Xs...");

// BepInEx automatically handles enum serialization
```

### Dynamic Config Options (Font List Example)

```csharp
private static string[] GetAvailableFonts()
{
    try
    {
        var fonts = Font.GetOSInstalledFontNames();
        if (fonts != null && fonts.Length > 0)
            return fonts.OrderBy(f => f).ToArray();
    }
    catch { }
    return new[] { "Arial", "Consolas", "Courier New" };
}

_fontName = Config.Bind("Appearance", "Font Name", "Arial",
    new ConfigDescription("...", 
        new AcceptableValueList<string>(GetAvailableFonts())));
```

---

## SPT 4.0 Structure Reference

### BepInEx Plugin Pattern

```csharp
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class MyPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.author.pluginname";
    internal static ManualLogSource Log;

    private void Awake()
    {
        Log = Logger;
        // Initialize config, patches, components
    }
}
```

### Assembly References (for .csproj)

```xml
<ItemGroup>
  <!-- BepInEx -->
  <Reference Include="BepInEx">
    <HintPath>$(BepInExDir)\core\BepInEx.dll</HintPath>
  </Reference>
  <Reference Include="0Harmony">
    <HintPath>$(BepInExDir)\core\0Harmony.dll</HintPath>
  </Reference>

  <!-- Unity -->
  <Reference Include="UnityEngine.CoreModule">
    <HintPath>$(ManagedDir)\UnityEngine.CoreModule.dll</HintPath>
  </Reference>
  <Reference Include="UnityEngine.IMGUIModule">
    <HintPath>$(ManagedDir)\UnityEngine.IMGUIModule.dll</HintPath>
  </Reference>

  <!-- EFT -->
  <Reference Include="Assembly-CSharp">
    <HintPath>$(ManagedDir)\Assembly-CSharp.dll</HintPath>
  </Reference>
  <Reference Include="Comfort">
    <HintPath>$(ManagedDir)\Comfort.dll</HintPath>
  </Reference>
</ItemGroup>
```

---

## Known EFT Classes & Enums (from DLL Inspection)

### Health Effect Body Parts

```csharp
enum EBodyPart
{
    Head, Chest, Stomach,
    LeftArm, RightArm,
    LeftLeg, RightLeg,
    Common  // Full-body effects
}
```

### Effect Categories (Observed in Buffs)

| Category | Examples |
|----------|----------|
| **Stimulators** | Adrenaline, Morphine, Propital, SJ6, Meldonin |
| **Pain Management** | Painkiller, Analgin, Ibuprofen |
| **Health Recovery** | Health regeneration, HealthRate |
| **Stamina** | Stamina recovery, Max stamina, StaminaRate |
| **Energy/Hydration** | EnergyRate, HydrationRate, Energy recovery |
| **Combat** | Damage reduction, Damage taken, Strength |
| **Injuries** | Bleeding, HeavyBleeding, Fracture, Contusion |
| **Status** | Exhaustion, Fatigue, Dehydration, Intoxication |
| **Skills** | Endurance, Strength, Metabolism, Perception |

### Health Effect Discovery (Reflection Targets)

```csharp
// Primary methods on IHealthController:
GetAllEffects() → IEnumerable<IEffect>
ActiveBuffsNames() → IEnumerable<string>

// Event fields (Action<IPlayerBuff>):
// - Field names vary (obfuscated), search by type signature
// - Pattern: Action<IPlayerBuff> for add, Action<IPlayerBuff> for remove
// - Field names may contain "1" for remove events

// Buff containers (GClass3056 pattern):
// - Has field: List<IPlayerBuff> Buffs
// - Has field: float TimeLeft
// - No explicit interface, discovered via field signature
```

---

## Display Effect Processing

### Name Cleanup Pipeline

```csharp
string ProcessBuffName(string rawName)
{
    // 1. Strip Unity rich-text tags
    string clean = Regex.Replace(rawName, @"<[^>]+>", "");
    
    // 2. Remove embedded value notation (e.g., "Health +30" → "Health")
    clean = Regex.Replace(clean, @"\s*[+\-][\d.]+%?$", "");
    
    // 3. Abbreviate if enabled
    if (_abbreviateNames.Value)
        clean = AbbreviateName(clean);
    
    return clean;
}

string AbbreviateName(string name)
{
    var abbrevs = new Dictionary<string, string>
    {
        { "regeneration", "regen" },
        { "recovery", "recov" },
        { "reduction", "reduct" },
        { "maximum", "max" },
        // ... more
    };
    
    foreach (var kv in abbrevs)
        name = Regex.Replace(name, kv.Key, kv.Value, 
            RegexOptions.IgnoreCase);
    
    return name;
}
```

### Value Extraction

```csharp
float GetEffectValue(object buff, string buffName)
{
    // 1. Try buff.Value property
    float val = GetFloatProp(buff, "Value");
    if (val != 0f) return val;
    
    // 2. Parse from name (e.g., "Health +30" → 30)
    var match = Regex.Match(buffName, @"([+\-])([\d.]+)%?$");
    if (match.Success)
    {
        float parsed = float.Parse(match.Groups[2].Value);
        return match.Groups[1].Value == "-" ? -parsed : parsed;
    }
    
    return 0f;
}
```

### Polarity Detection

```csharp
bool IsNegativeEffect(object buff, string buffName, float value)
{
    // 1. Check for red color tag in name (EFT convention)
    if (buffName.Contains("#C40000")) return true;
    
    // 2. Check value sign
    if (value < 0) return true;
    
    // 3. Known negative keywords
    if (Regex.IsMatch(buffName, @"\b(bleeding|fracture|pain|exhaustion|dehydration)\b", 
        RegexOptions.IgnoreCase))
        return true;
    
    return false;
}
```

---

## Packaging & Distribution

### Archive Structure

```
MedEffectsHUD-v1.1.3.zip
└── BepInEx/
    └── plugins/
        └── MedEffectsHUD/
            ├── MedEffectsHUD.dll
            └── icons/
                ├── *.png              (73 shared icons)
                ├── positive/          (9 polarity-specific)
                └── negative/          (11 polarity-specific)
```

**Critical:** Use **forward slashes** in ZIP paths, not backslashes. Windows PowerShell `Compress-Archive` produces backslashes and will break Dragon Den installer.

### Build Script (Python for correct ZIP format)

```python
import os, zipfile

base_dir = r"E:\CustomMods\MedEffectsHUD\dist"
zip_path = r"E:\CustomMods\MedEffectsHUD\MedEffectsHUD-v1.1.3.zip"

with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
    for root, dirs, files in os.walk(base_dir):
        for name in files:
            full = os.path.join(root, name)
            rel = os.path.relpath(full, base_dir).replace(os.sep, "/")
            zf.write(full, rel)
```

---

## Debugging Tips

### Logging Best Practices

```csharp
// Use conditional logging to avoid spam
if (_tick < 100)
    Log.LogInfo($"[S1+] Buff ADDED: {key}");

// Use descriptive prefixes for different discovery strategies
Log.LogInfo($"[S1] Event subscription");      // Strategy 1
Log.LogInfo($"[S2] Deep scan");               // Strategy 2
Log.LogInfo($"[S3] IReadOnlyList_0");         // Strategy 3
Log.LogInfo($"[S4] GetAllEffects");           // Strategy 4

// Periodic summary logging
if (_tick % 20 == 1)
    Log.LogDebug($"[Refresh] buffs={buffCount} pos={_positiveEffects.Count} neg={_negativeEffects.Count}");
```

### Testing Checklist

- [x] **Stim reapplication** — Use same stim twice, verify timer resets correctly
- [x] **Long-duration buffs** — Verify timers don't freeze at 999999
- [x] **Skill effects** — Level up skills in raid, check display
- [x] **Injuries** — Test bleeding, fractures, contusions
- [x] **Map transitions** — GUI styles rebuild, no memory leaks
- [x] **Icon fallback** — Remove icon file, verify text-only display works
- [x] **Blacklist** — Add effect to blacklist, confirm it's hidden
- [x] **Blink** — Set threshold high, verify expiring effects blink

---

## Common Issues & Solutions

### Issue: Buffs Not Detected

**Causes:**
- Health controller not initialized yet
- Event subscription failed (obfuscated type changed)
- Deep scan depth too shallow

**Solutions:**
1. Check log for `[Init] Health controller` message
2. Increase `maxDepth` in `DeepScan()` (default 5)
3. Add more strategy layers (IReadOnlyList_0, etc.)

### Issue: Timers Freezing at High Values

**Cause:** Using `WholeTime` directly without offset correction.

**Solution:** Always subtract `_buffWholeTimeOffset` when computing remaining time.

### Issue: Icons Not Loading

**Causes:**
- DLL in wrong location (affects icons folder path resolution)
- Filename mismatch (case-sensitive on Linux servers)

**Solutions:**
1. Check log: `[Icons] Icons folder: {path} (exists={bool})`
2. Use lowercase filenames consistently
3. Verify icon resolution order: polarity-specific → root → display name

### Issue: GUI Broken After Map Change

**Cause:** Unity destroyed GUI styles during scene transition.

**Solution:** Reset `_stylesInit = false` in `DoReset()`.

---

## Performance Considerations

### Update Frequency

```csharp
private const float UpdateInterval = 0.5f;  // 500ms refresh
private float _lastUpdateTime;

if (Time.time - _lastUpdateTime < UpdateInterval) return;
_lastUpdateTime = Time.time;
```

### Deep Scan Throttling

```csharp
// Run full deep scan only once per map
if (!_deepScanDone) DeepScanHealthController();

// Periodic quick rescan every 1.5s (3 ticks × 0.5s)
if (_tick % 3 == 0)
    QuickRescan(visited);
```

### Icon Cache Management

```csharp
// Icons are cached forever (no unload needed — small memory footprint)
private readonly Dictionary<string, Texture2D> _iconCache;

// Missing icon tracking prevents repeated file I/O
private readonly HashSet<string> _iconMissing;
```

---

## Future Enhancement Ideas

1. **Custom Icon Packs** — Allow users to drop in themed icon folders
2. **Effect Grouping** — Combine same effect on multiple body parts
3. **Sound Notifications** — Audio cue for critical debuffs
4. **Drag & Drop HUD** — Mouse-based positioning
5. **Effect History** — Show recently expired effects (faded)
6. **Battle Timer Integration** — Sync blink speed with effect timer
7. **Multiplayer Awareness** — Detect co-op mode, adjust accordingly
8. **Profile-Specific Configs** — Different layouts per PMC/Scav

---

## References

- **SPT Hub:** https://hub.sp-tarkov.com/
- **BepInEx Docs:** https://docs.bepinex.dev/
- **EFT SDK:** https://github.com/S3RAPH-1M/EscapeFromTarkov-SDK
- **Harmony Patches:** https://harmony.pardeike.net/

---

## Contributing

When modifying this mod:

1. **Preserve backward compatibility** — Don't remove config entries
2. **Log strategically** — Use `_tick` guards to avoid spam
3. **Test reapplication** — Always verify stim re-use cases
4. **Document reflection** — Comment why each reflection lookup exists
5. **Follow ZIP format** — Use Python script for packaging

---

## License

MIT License — See [LICENSE](LICENSE) file.
