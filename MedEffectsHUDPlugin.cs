using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using UnityEngine;

namespace MedEffectsHUD
{
    /// <summary>
    /// In-raid HUD overlay that displays active buffs and debuffs in two colour-coded columns.
    /// Uses multiple strategies (event subscription, deep reflection scan, GetAllEffects)
    /// to reliably discover all active IPlayerBuff and health-effect instances at runtime.
    /// Fully configurable via BepInEx config (F5 in-game with ConfigurationManager).
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class MedEffectsHUDPlugin : BaseUnityPlugin
    {
        public const string PluginGuid    = "com.koloskovnick.medeffectshud";
        public const string PluginName    = "MedEffectsHUD";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        // Config — General
        private ConfigEntry<KeyboardShortcut> _toggleKey;
        // Config — Position
        private ConfigEntry<float> _hudX, _hudY, _hudWidth;
        // Config — Appearance
        private ConfigEntry<int>   _fontSize;
        private ConfigEntry<float> _backgroundAlpha;
        // Config — Display
        private ConfigEntry<TimeFormat> _timeFormat;
        private ConfigEntry<bool> _showValues;
        private ConfigEntry<bool> _showTime;
        // Config — Layout
        private ConfigEntry<float> _lineSpacing;
        private ConfigEntry<bool>  _showTitle;
        private ConfigEntry<bool>  _showColumnHeaders;
        private ConfigEntry<bool>  _hideEmptyColumns;
        private ConfigEntry<bool>  _abbreviateNames;
        private ConfigEntry<bool>  _sortByTime;
        private ConfigEntry<EffectLayout> _effectLayout;
        // Config — Filter
        private ConfigEntry<string> _blacklist;
        private HashSet<string> _blacklistSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Config — Colors
        private ConfigEntry<string> _positiveColor;
        private ConfigEntry<string> _negativeColor;
        private ConfigEntry<string> _valueColor;
        private ConfigEntry<string> _timeNormalColor;
        private ConfigEntry<string> _timeExpiringColor;
        private ConfigEntry<float>  _timeColorThreshold;
        private ConfigEntry<string> _effectColorOverrides;
        private Dictionary<string, string> _colorOverrideMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Config — Blink
        private ConfigEntry<bool>  _blinkEnabled;
        private ConfigEntry<float> _blinkThreshold;
        private ConfigEntry<float> _blinkSpeed;

        /// <summary>Time display format.</summary>
        public enum TimeFormat { Auto, SecondsOnly, MinutesOnly }

        /// <summary>How to arrange positive and negative columns.</summary>
        public enum EffectLayout
        {
            /// <summary>Positive left, negative right (two columns).</summary>
            SideBySide,
            /// <summary>Negative effects listed below positive (single column).</summary>
            Stacked
        }

        private bool _hudVisible = true;
        private Player _localPlayer;
        private IHealthController _healthController;
        private object _cachedHC;

        // IPlayerBuff type resolved at runtime
        private Type _ipbType;
        private bool _ipbSearched;

        // Event-driven captured buffs (buff object references, keyed by unique string)
        private bool _eventsSubscribed;
        private readonly Dictionary<string, object> _capturedBuffs =
            new Dictionary<string, object>(StringComparer.Ordinal);

        // Buff → parent container mapping (container has TimeLeft)
        private readonly Dictionary<int, object> _buffToContainer =
            new Dictionary<int, object>();
        // Tracked buff containers
        private readonly HashSet<int> _containerIds = new HashSet<int>();
        private readonly List<object> _containers = new List<object>();

        // Deep scan done flag
        private bool _deepScanDone;

        // One-shot time diagnostics: fires once when we have actual buffs
        private bool _timeDiagDone;

        // Display
        private readonly List<DisplayEffect> _positiveEffects = new List<DisplayEffect>();
        private readonly List<DisplayEffect> _negativeEffects = new List<DisplayEffect>();
        private float _lastUpdateTime;
        private const float UpdateInterval = 0.5f;
        private int _tick;

        // GUI
        private GUIStyle _boxStyle, _titleStyle, _colHeaderStyle;
        private GUIStyle _posStyle, _negStyle, _dimStyle, _sepStyle;
        private bool _stylesInit;

        // ================================================================
        //  LIFECYCLE
        // ================================================================

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading…");
            _toggleKey       = Config.Bind("General", "Toggle Key", new KeyboardShortcut(KeyCode.F8));
            _hudX            = Config.Bind("Position", "X", 10f);
            _hudY            = Config.Bind("Position", "Y", 200f);
            _hudWidth        = Config.Bind("Position", "Width", 560f);
            _fontSize        = Config.Bind("Appearance", "Font Size", 13);
            _backgroundAlpha = Config.Bind("Appearance", "Background Alpha", 0.85f,
                new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            // Display
            _timeFormat  = Config.Bind("Display", "Time Format", TimeFormat.Auto,
                "Auto = Xm XXs when ≥60s else Xs. SecondsOnly = always Xs. MinutesOnly = always Xm XXs.");
            _showValues  = Config.Bind("Display", "Show Values", true,
                "Show numeric values like (+30), (-1.2) next to buff names.");
            _showTime    = Config.Bind("Display", "Show Time", true,
                "Show remaining time next to buffs.");
            // Layout
            _lineSpacing = Config.Bind("Layout", "Line Spacing", 2f,
                new ConfigDescription("Extra vertical space (px) between effect lines.",
                    new AcceptableValueRange<float>(0f, 20f)));
            _showTitle = Config.Bind("Layout", "Show Title", false,
                "Show the 'PLAYER BUFFS' title bar.");
            _showColumnHeaders = Config.Bind("Layout", "Show Column Headers", false,
                "Show POSITIVE / NEGATIVE column headers.");
            _hideEmptyColumns = Config.Bind("Layout", "Hide Empty Columns", true,
                "Hide a column entirely when it has no effects. If both are empty, hide the HUD.");
            _abbreviateNames = Config.Bind("Layout", "Abbreviate Names", true,
                "Shorten long buff/debuff names (e.g. 'Health regeneration' → 'Health regen').");
            _sortByTime = Config.Bind("Layout", "Sort By Time", true,
                "Sort effects ascending by remaining time (expiring soonest at the top).");
            _effectLayout = Config.Bind("Layout", "Effect Layout", EffectLayout.SideBySide,
                "SideBySide = positive left, negative right (two columns).\n"
                + "Stacked = negative effects listed below positive (single column).");
            // Filter
            _blacklist = Config.Bind("Filter", "Blacklist", "",
                "Comma-separated list of effect names to hide (case-insensitive, partial match).\n"
                + "Example: LowEdgeHealth, Painkiller, Tremor");
            _blacklist.SettingChanged += (_, __) => RebuildBlacklist();
            RebuildBlacklist();
            // Colors
            _positiveColor = Config.Bind("Colors", "Positive Name Color", "#4DFF4D",
                "Hex color (#RRGGBB) for positive effect names.");
            _negativeColor = Config.Bind("Colors", "Negative Name Color", "#FF5959",
                "Hex color (#RRGGBB) for negative effect names.");
            _valueColor = Config.Bind("Colors", "Value Color", "#FFFFFF",
                "Hex color (#RRGGBB) for the numeric value part like (+30), (-1.2).");
            _timeNormalColor = Config.Bind("Colors", "Time Normal Color", "#A0A0FF",
                "Hex color for remaining time when ABOVE the threshold.");
            _timeExpiringColor = Config.Bind("Colors", "Time Expiring Color", "#FFD700",
                "Hex color for remaining time when BELOW the threshold.");
            _timeColorThreshold = Config.Bind("Colors", "Time Threshold Seconds", 30f,
                new ConfigDescription(
                    "Effects with less than this many seconds remaining use 'Time Expiring Color'.",
                    new AcceptableValueRange<float>(0f, 600f)));
            _effectColorOverrides = Config.Bind("Colors", "Effect Color Overrides", "",
                "Per-effect custom name color. Format: Name:#RRGGBB, Name2:#RRGGBB\n"
                + "Partial match, case-insensitive. Example: Painkiller:#FFD700,Tremor:#FF00FF");
            _effectColorOverrides.SettingChanged += (_, __) => RebuildColorOverrides();
            RebuildColorOverrides();
            // Blink
            _blinkEnabled   = Config.Bind("Blink", "Enabled", true,
                "Enable blinking for buffs/debuffs about to expire.");
            _blinkThreshold = Config.Bind("Blink", "Threshold Seconds", 10f,
                new ConfigDescription("Start blinking when less than this many seconds remain.",
                    new AcceptableValueRange<float>(0f, 120f)));
            _blinkSpeed     = Config.Bind("Blink", "Speed", 3f,
                new ConfigDescription("Blink frequency in Hz.",
                    new AcceptableValueRange<float>(0.5f, 10f)));
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        /// <summary>Rebuild the blacklist HashSet from the config string.</summary>
        private void RebuildBlacklist()
        {
            _blacklistSet.Clear();
            if (string.IsNullOrEmpty(_blacklist.Value)) return;
            foreach (var entry in _blacklist.Value.Split(','))
            {
                var trimmed = entry.Trim();
                if (trimmed.Length > 0)
                    _blacklistSet.Add(trimmed);
            }
            Log.LogInfo($"[Filter] Blacklist: {_blacklistSet.Count} entries");
        }

        /// <summary>Check if an effect name is blacklisted (partial, case-insensitive).</summary>
        private bool IsBlacklisted(string name)
        {
            if (_blacklistSet.Count == 0 || string.IsNullOrEmpty(name)) return false;
            foreach (var bl in _blacklistSet)
            {
                if (name.IndexOf(bl, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>Rebuild per-effect color override map from config string.</summary>
        private void RebuildColorOverrides()
        {
            _colorOverrideMap.Clear();
            if (string.IsNullOrEmpty(_effectColorOverrides.Value)) return;
            foreach (var pair in _effectColorOverrides.Value.Split(','))
            {
                var trimmed = pair.Trim();
                int colonIdx = trimmed.LastIndexOf(':');
                if (colonIdx <= 0 || colonIdx >= trimmed.Length - 1) continue;
                string name  = trimmed.Substring(0, colonIdx).Trim();
                string color = trimmed.Substring(colonIdx + 1).Trim();
                if (name.Length == 0 || color.Length == 0) continue;
                if (!color.StartsWith("#")) color = "#" + color;
                _colorOverrideMap[name] = color;
            }
            Log.LogInfo($"[Colors] Effect overrides: {_colorOverrideMap.Count} entries");
        }

        /// <summary>Get the hex color for a specific effect name, checking overrides first.</summary>
        private string GetEffectNameColor(string name, bool isPositive)
        {
            if (_colorOverrideMap.Count > 0 && !string.IsNullOrEmpty(name))
            {
                // Exact match first
                if (_colorOverrideMap.TryGetValue(name, out string exact))
                    return exact;
                // Partial match
                foreach (var kv in _colorOverrideMap)
                {
                    if (name.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return kv.Value;
                }
            }
            return isPositive ? _positiveColor.Value : _negativeColor.Value;
        }

        // ================================================================
        //  UPDATE
        // ================================================================

        private void Update()
        {
            if (_toggleKey.Value.IsDown()) _hudVisible = !_hudVisible;
            if (!_hudVisible) return;
            if (Time.time - _lastUpdateTime < UpdateInterval) return;
            _lastUpdateTime = Time.time;

            UpdatePlayerRef();
            if (_healthController != null) RefreshEffects();
        }

        private void UpdatePlayerRef()
        {
            try
            {
                if (_localPlayer != null && _localPlayer.HealthController != null) return;
                if (!Singleton<GameWorld>.Instantiated) { DoReset(); return; }
                var gw = Singleton<GameWorld>.Instance;
                if (gw == null) { DoReset(); return; }
                _localPlayer = gw.allAlivePlayersByID?.Values.FirstOrDefault(p => p.IsYourPlayer);
                _healthController = _localPlayer?.HealthController;
                if (_healthController != null && !ReferenceEquals(_cachedHC, _healthController))
                {
                    _cachedHC = _healthController;
                    _eventsSubscribed = false;
                    _deepScanDone     = false;
                    _timeDiagDone     = false;
                    _capturedBuffs.Clear();
                    _buffToContainer.Clear();
                    _containerIds.Clear();
                    _containers.Clear();
                    Log.LogInfo($"[Init] Health controller: {_healthController.GetType().FullName}");
                }
            }
            catch { DoReset(); }
        }

        private void DoReset()
        {
            _localPlayer = null; _healthController = null;
            _eventsSubscribed = false; _deepScanDone = false; _timeDiagDone = false;
            _capturedBuffs.Clear();
            _buffToContainer.Clear();
            _containerIds.Clear();
            _containers.Clear();
        }

        // ================================================================
        //  RESOLVE IPlayerBuff AT RUNTIME
        // ================================================================

        private void ResolveIPB()
        {
            if (_ipbSearched) return;
            _ipbSearched = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.FullName.Contains("Assembly-CSharp")) continue;
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == "IPlayerBuff" && t.IsInterface)
                        {
                            _ipbType = t;
                            Log.LogInfo($"[Resolve] IPlayerBuff = {t.FullName}");
                            return;
                        }
                    }
                }
                Log.LogWarning("[Resolve] IPlayerBuff not found!");
            }
            catch (Exception ex) { Log.LogError($"[Resolve] {ex.Message}"); }
        }

        // ================================================================
        //  EVENT SUBSCRIPTION
        // ================================================================

        private void SubscribeEvents()
        {
            if (_eventsSubscribed) return;
            _eventsSubscribed = true;
            ResolveIPB();
            if (_ipbType == null) { Log.LogWarning("[S1] No IPlayerBuff type, skip events"); return; }

            try
            {
                object hc = _healthController;
                var actionType = typeof(Action<>).MakeGenericType(_ipbType);
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                var t = hc.GetType();
                while (t != null && t != typeof(object))
                {
                    foreach (var f in t.GetFields(flags | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType != actionType) continue;

                        bool isRemove = f.Name.Contains("1") || f.Name.ToLower().Contains("remove");

                        // Build Action<IPlayerBuff> delegate pointing to our handler
                        var handler = BuildActionDelegate(isRemove);
                        if (handler == null) continue;

                        var existing = (Delegate)f.GetValue(hc);
                        var combined = Delegate.Combine(existing, handler);
                        f.SetValue(hc, combined);
                        Log.LogInfo($"[S1] Subscribed {t.Name}.{f.Name} (isRemove={isRemove})");
                    }
                    t = t.BaseType;
                }
            }
            catch (Exception ex) { Log.LogError($"[S1] {ex}"); }
        }

        /// <summary>Creates a dynamically-typed Action&lt;IPlayerBuff&gt; delegate.</summary>
        private Delegate BuildActionDelegate(bool isRemove)
        {
            try
            {
                var actionType = typeof(Action<>).MakeGenericType(_ipbType);

                // Our target method accepts 'object', so we need a wrapper.
                // Use a closure via a helper class.
                var wrapper = new BuffEventWrapper(this, isRemove);
                var mi = typeof(BuffEventWrapper).GetMethod("Handle");
                return Delegate.CreateDelegate(actionType, wrapper, mi);
            }
            catch (Exception ex)
            {
                Log.LogError($"[S1] BuildDelegate: {ex.Message}");
                return null;
            }
        }

        /// <summary>Helper class whose Handle method signature matches Action&lt;IPlayerBuff&gt; at runtime.</summary>
        internal class BuffEventWrapper
        {
            private readonly MedEffectsHUDPlugin _plugin;
            private readonly bool _isRemove;

            public BuffEventWrapper(MedEffectsHUDPlugin plugin, bool isRemove)
            {
                _plugin = plugin; _isRemove = isRemove;
            }

            public void Handle(object buff)
            {
                if (_isRemove) _plugin.OnBuffRemovedObj(buff);
                else           _plugin.OnBuffAddedObj(buff);
            }
        }

        internal void OnBuffAddedObj(object buff)
        {
            try
            {
                string key = GetBuffKey(buff);
                if (string.IsNullOrEmpty(key)) return;
                _capturedBuffs[key] = buff;
                if (_tick < 100)
                    Log.LogInfo($"[S1+] Buff ADDED: {key}");
            }
            catch { }
        }

        internal void OnBuffRemovedObj(object buff)
        {
            // We intentionally keep references here and prune via the Active
            // property during RefreshEffects, because add/remove events fire
            // in quick succession.
            try
            {
                if (_tick < 100)
                {
                    string key = GetBuffKey(buff);
                    Log.LogInfo($"[S1~] Buff notif (Action_1): {key}");
                }
            }
            catch { }
        }

        // ================================================================
        //  DEEP RECURSIVE SCAN
        // ================================================================

        private void DeepScanHealthController()
        {
            if (_deepScanDone) return;
            _deepScanDone = true;
            ResolveIPB();

            Log.LogInfo("[S2] Starting deep scan…");
            int before = _capturedBuffs.Count;
            var visited = new HashSet<int>();

            // Main deep scan
            DeepScan(_healthController, 0, 5, visited, "HC");

            // Try IReadOnlyList_0
            TryReadIReadOnlyList(visited);

            // Scan each IEffect for embedded buffs
            ScanEffectsForBuffs(visited);

            // ActiveBuffsNames diagnostic
            LogActiveBuffsNames();

            int added = _capturedBuffs.Count - before;
            Log.LogInfo($"[S2] Deep scan complete. New buffs found: {added}, total: {_capturedBuffs.Count}");
        }

        /// <summary>Recursive deep scan for IPlayerBuff objects and GClass3056 containers.</summary>
        private int DeepScan(object obj, int depth, int maxDepth, HashSet<int> visited, string path)
        {
            if (obj == null || depth > maxDepth) return 0;

            int id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            if (visited.Contains(id)) return 0;
            visited.Add(id);

            var objType = obj.GetType();
            if (objType.IsPrimitive || objType.IsEnum || objType == typeof(string)) return 0;
            if (typeof(Delegate).IsAssignableFrom(objType)) return 0;
            if (objType.Namespace != null && objType.Namespace.StartsWith("UnityEngine")) return 0;

            int found = 0;

            // Check: is THIS object an IPlayerBuff?
            if (IsIPB(obj))
            {
                found += CaptureBuffFromScan(obj, path);
                return found; // don't recurse further into buff internals
            }

            // Check: is THIS object a buff container (has Buffs list + TimeLeft)?
            found += TryReadBuffContainer(obj, path);

            // Iterate fields
            try
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                foreach (var f in objType.GetFields(flags))
                {
                    try
                    {
                        if (f.IsStatic) continue;
                        if (f.FieldType.IsPrimitive || f.FieldType.IsEnum
                            || f.FieldType == typeof(string)) continue;
                        if (typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;

                        var val = f.GetValue(obj);
                        if (val == null) continue;

                        // Collection: iterate elements
                        if (val is IList list)
                        {
                            int cnt = Math.Min(list.Count, 300);
                            for (int i = 0; i < cnt; i++)
                            {
                                var item = list[i];
                                if (item != null)
                                    found += DeepScan(item, depth + 1, maxDepth, visited,
                                        $"{path}.{f.Name}[{i}]");
                            }
                            continue;
                        }
                        if (val is IDictionary dict)
                        {
                            foreach (DictionaryEntry de in dict)
                            {
                                if (de.Value != null)
                                    found += DeepScan(de.Value, depth + 1, maxDepth, visited,
                                        $"{path}.{f.Name}[D]");
                            }
                            continue;
                        }

                        // Non-collection object: recurse into it
                        found += DeepScan(val, depth + 1, maxDepth, visited, $"{path}.{f.Name}");
                    }
                    catch { }
                }
            }
            catch { }

            return found;
        }

        /// <summary>Try to read an object as a buff container (has Buffs list + TimeLeft).</summary>
        private int TryReadBuffContainer(object obj, string path)
        {
            if (obj == null) return 0;
            int found = 0;
            try
            {
                var t = obj.GetType();
                var buffsField = t.GetField("Buffs", BindingFlags.Public | BindingFlags.Instance);
                if (buffsField == null) return 0;

                var buffsList = buffsField.GetValue(obj) as IList;
                if (buffsList == null || buffsList.Count == 0) return 0;

                // Register container
                int cid = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
                if (!_containerIds.Contains(cid))
                {
                    _containerIds.Add(cid);
                    _containers.Add(obj);
                }

                float timeLeft = GetFloatProp(obj, "TimeLeft");

                foreach (var buff in buffsList)
                {
                    if (buff == null) continue;
                    // Map buff → container
                    int bid = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(buff);
                    _buffToContainer[bid] = obj;
                    found += CaptureBuffFromScan(buff, $"{path}.Buffs");
                }

                if (found > 0 && _tick < 100)
                    Log.LogInfo($"[BuffCont] {path}: {found} buffs, timeLeft={timeLeft:F0}");
            }
            catch { }
            return found;
        }

        private int CaptureBuffFromScan(object buff, string path)
        {
            if (!IsIPB(buff) && !HasIPBProps(buff.GetType())) return 0;
            string key = GetBuffKey(buff);
            if (string.IsNullOrEmpty(key)) return 0;
            if (_capturedBuffs.ContainsKey(key)) return 0;

            _capturedBuffs[key] = buff;
            if (_tick < 200)
                Log.LogInfo($"[Scan] {path}: {key}");
            return 1;
        }

        /// <summary>Get the remaining time for a buff (tries Duration−Elapsed, container, direct).</summary>
        private float GetBuffTimeLeft(object buff)
        {
            // 1) Try computing from Settings.Duration - WholeTime (Class2222 buffs from stimulators)
            float duration = GetSettingsDuration(buff);
            if (duration > 0)
            {
                float elapsed = GetFloatProp(buff, "WholeTime");
                if (elapsed >= 0)
                {
                    float remaining = duration - elapsed;
                    if (remaining > 0 && remaining < 100000f) return remaining;
                    if (remaining <= 0) return 0f; // expired
                }
            }

            // 2) Try parent container's TimeLeft (GClass3056)
            int bid = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(buff);
            if (_buffToContainer.TryGetValue(bid, out object container))
            {
                float tl = GetFloatProp(container, "TimeLeft");
                if (tl > 0 && tl < 100000f) return tl;
            }

            // 3) Try buff's own TimeLeft
            float directTL = GetFloatProp(buff, "TimeLeft");
            if (directTL > 0 && directTL < 100000f) return directTL;

            return -1f;
        }

        /// <summary>Read Duration from buff's Settings object.</summary>
        private float GetSettingsDuration(object buff)
        {
            try
            {
                var settingsProp = buff.GetType().GetProperty("Settings",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (settingsProp == null) return -1f;
                var settings = settingsProp.GetValue(buff);
                if (settings == null) return -1f;
                return GetFloatProp(settings, "Duration");
            }
            catch { return -1f; }
        }

        /// <summary>Try IReadOnlyList_0 property on the health controller.</summary>
        private void TryReadIReadOnlyList(HashSet<int> visited)
        {
            try
            {
                var prop = _healthController.GetType().GetProperty("IReadOnlyList_0",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null) return;

                var val = prop.GetValue(_healthController);
                if (val is IEnumerable items)
                {
                    int cnt = 0;
                    foreach (var item in items)
                    {
                        if (item == null) continue;
                        cnt++;
                        DeepScan(item, 0, 3, visited, "S3.ROList");
                    }
                    Log.LogInfo($"[S3] IReadOnlyList_0: {cnt} items");
                }
            }
            catch (Exception ex) { Log.LogWarning($"[S3] {ex.Message}"); }
        }

        /// <summary>Scan each IEffect from GetAllEffects for embedded buff data.</summary>
        private void ScanEffectsForBuffs(HashSet<int> visited)
        {
            try
            {
                var m = _healthController.GetType().GetMethod("GetAllEffects", new Type[0]);
                if (m == null) return;
                var effects = m.Invoke(_healthController, null) as IEnumerable;
                if (effects == null) return;

                foreach (var fx in effects)
                {
                    if (fx == null) continue;
                    string name = GetCleanEffectName(fx.GetType());
                    int f = DeepScan(fx, 0, 3, visited, $"S4.{name}");
                    if (f > 0)
                        Log.LogInfo($"[S4] Effect '{name}' has {f} embedded buffs");
                }
            }
            catch (Exception ex) { Log.LogWarning($"[S4] {ex.Message}"); }
        }

        /// <summary>Log ActiveBuffsNames() as diagnostic ground truth.</summary>
        private void LogActiveBuffsNames()
        {
            try
            {
                var m = _healthController.GetType().GetMethod("ActiveBuffsNames",
                    BindingFlags.Public | BindingFlags.Instance);
                if (m == null) return;
                var result = m.Invoke(_healthController, null);
                if (result == null) { Log.LogInfo("[S6] ActiveBuffsNames = null"); return; }

                if (result is IEnumerable<string> names)
                {
                    var list = names.ToList();
                    Log.LogInfo($"[S6] ActiveBuffsNames ({list.Count}): [{string.Join(", ", list)}]");
                }
                else if (result is IEnumerable en)
                {
                    var items = new List<string>();
                    foreach (var n in en) items.Add(n?.ToString() ?? "null");
                    Log.LogInfo($"[S6] ActiveBuffsNames ({items.Count}): [{string.Join(", ", items)}]");
                }
                else
                {
                    Log.LogInfo($"[S6] ActiveBuffsNames returned {result.GetType().Name}: {result}");
                }
            }
            catch (Exception ex) { Log.LogWarning($"[S6] {ex.Message}"); }
        }

        // ================================================================
        //  EFFECT REFRESH (called every tick)
        // ================================================================

        private void RefreshEffects()
        {
            _positiveEffects.Clear();
            _negativeEffects.Clear();

            var seenPos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenNeg = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_healthController == null) return;
            _tick++;

            // One-time init
            if (!_eventsSubscribed) SubscribeEvents();
            if (!_deepScanDone)     DeepScanHealthController();

            // Periodic re-scan for newly appeared buff containers (every 1.5 s)
            if (_tick % 3 == 0)
            {
                var visited = new HashSet<int>();
                QuickRescan(visited);
            }

            // ---- Read captured IPlayerBuff objects ----
            int buffCount = 0;
            var deadKeys = new List<string>();

            foreach (var kv in _capturedBuffs)
            {
                try
                {
                    var buff = kv.Value;
                    if (buff == null) { deadKeys.Add(kv.Key); continue; }

                    bool active = GetBoolProp(buff, "Active", true);
                    if (!active) { deadKeys.Add(kv.Key); continue; }

                    string buffName = GetStringProp(buff, "BuffName");
                    if (string.IsNullOrEmpty(buffName)) continue;

                    float value    = GetFloatProp(buff, "Value");
                    float timeLeft = SanitizeTimer(GetBuffTimeLeft(buff));

                    // One-time diagnostic: verify remaining time calculation
                    if (!_timeDiagDone)
                    {
                        float wt = GetFloatProp(buff, "WholeTime");
                        float dur = GetSettingsDuration(buff);
                        Log.LogInfo($"[TIME] {StripColorTags(buffName).Substring(0, Math.Min(25, StripColorTags(buffName).Length))}: " +
                            $"Duration={dur:F1} WholeTime(elapsed)={wt:F2} " +
                            $"remaining={timeLeft:F1}");
                    }

                    // Determine positive/negative from the color tag (#C40000 = red = neg)
                    bool isNegBuff = buffName.Contains("#C40000");

                    // Strip Unity rich-text <color> tags
                    string cleanName = StripColorTags(buffName);
                    string display = StripValueFromName(cleanName);

                    // If Value property is 0, try to parse it from the name
                    if (value == 0f)
                        value = ParseValueFromName(cleanName);

                    // Override sign from color tag
                    if (isNegBuff && value > 0) value = -value;

                    // Skip blacklisted effects
                    if (IsBlacklisted(display)) continue;

                    string ukey = display + "|" + (isNegBuff ? "N" : "P");

                    var de = new DisplayEffect { Name = display, Time = timeLeft, Strength = value };

                    if (!isNegBuff)
                        AddDedup(_positiveEffects, seenPos, de, ukey);
                    else
                        AddDedup(_negativeEffects, seenNeg, de, ukey);

                    buffCount++;
                }
                catch { }
            }
            // Mark time diag as done if we logged at least one buff
            if (!_timeDiagDone && buffCount > 0)
            {
                _timeDiagDone = true;
                Log.LogInfo($"[TIME_DIAG] Done. Buff timer uses Settings.Duration - WholeTime.");
            }

            foreach (var dk in deadKeys) _capturedBuffs.Remove(dk);

            // ---- Health effects from GetAllEffects() ----
            int fxCount = ReadHealthEffects(seenPos, seenNeg);

            // Sort by time remaining if enabled
            if (_sortByTime.Value)
            {
                _positiveEffects.Sort(CompareByTime);
                _negativeEffects.Sort(CompareByTime);
            }

            if (_tick % 20 == 1)
                Log.LogDebug($"[Refresh] buffs={buffCount} fx={fxCount} " +
                             $"pos={_positiveEffects.Count} neg={_negativeEffects.Count} " +
                             $"captured={_capturedBuffs.Count}");
        }

        /// <summary>Periodic quick re-scan of known container fields.</summary>
        private void QuickRescan(HashSet<int> visited)
        {
            try
            {
                var hcType = _healthController.GetType();
                var flags  = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

                var t = hcType;
                while (t != null && t != typeof(object))
                {
                    // List_0 (Class2234 containers)
                    var f0 = t.GetField("List_0", flags);
                    if (f0 != null)
                    {
                        var list = f0.GetValue(_healthController) as IList;
                        if (list != null)
                            foreach (var c in list)
                                if (c != null) DeepScan(c, 0, 3, visited, "QS.L0");
                    }

                    // Dictionary_1
                    var d1 = t.GetField("Dictionary_1", flags);
                    if (d1 != null)
                    {
                        var dict = d1.GetValue(_healthController) as IDictionary;
                        if (dict != null)
                            foreach (DictionaryEntry de in dict)
                                if (de.Value != null) DeepScan(de.Value, 0, 3, visited, "QS.D1");
                    }

                    // List_1, List_2 (direct effect lists)
                    foreach (var ln in new[] { "List_1", "List_2" })
                    {
                        var fl = t.GetField(ln, flags);
                        if (fl == null) continue;
                        var lst = fl.GetValue(_healthController) as IList;
                        if (lst == null) continue;
                        foreach (var item in lst)
                            if (item != null) DeepScan(item, 0, 3, visited, $"QS.{ln}");
                    }

                    t = t.BaseType;
                }

                // IReadOnlyList_0
                try
                {
                    var prop = hcType.GetProperty("IReadOnlyList_0",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var val = prop.GetValue(_healthController);
                        if (val is IEnumerable items)
                            foreach (var item in items)
                                if (item != null) DeepScan(item, 0, 2, visited, "QS.RO");
                    }
                }
                catch { }

                // Re-read all tracked containers to re-map buff→container
                RefreshContainerMappings();
            }
            catch { }
        }

        /// <summary>Re-iterate all tracked GClass3056 containers and update buff→container links.</summary>
        private void RefreshContainerMappings()
        {
            try
            {
                foreach (var container in _containers)
                {
                    if (container == null) continue;
                    try
                    {
                        var t = container.GetType();
                        var buffsField = t.GetField("Buffs", BindingFlags.Public | BindingFlags.Instance);
                        if (buffsField == null) continue;
                        var buffsList = buffsField.GetValue(container) as IList;
                        if (buffsList == null) continue;
                        foreach (var buff in buffsList)
                        {
                            if (buff == null) continue;
                            int bid = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(buff);
                            _buffToContainer[bid] = container;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ================================================================
        //  HEALTH EFFECTS (GetAllEffects — PainKiller, Bleeding, etc.)
        // ================================================================

        private int ReadHealthEffects(HashSet<string> seenPos, HashSet<string> seenNeg)
        {
            int count = 0;
            try
            {
                var m = _healthController.GetType().GetMethod("GetAllEffects", new Type[0]);
                if (m == null) return 0;
                var effects = m.Invoke(_healthController, null) as IEnumerable;
                if (effects == null) return 0;

                foreach (var fx in effects)
                {
                    if (fx == null) continue;
                    var entry = ExtractEffectEntry(fx);
                    if (entry == null) continue;
                    if (IsUselessEffect(entry.EffectTypeName)) continue;

                    // Diagnostic: log all time properties from health effects (fires once)
                    if (!_timeDiagDone)
                    {
                        float rawTL = GetFloatProp(fx, "TimeLeft");
                        float rawWT = GetFloatProp(fx, "WorkTime");
                        float rawStr = GetFloatProp(fx, "Strength");
                        Log.LogInfo($"[FX_TIME] {entry.EffectTypeName}: " +
                            $"TimeLeft={rawTL:F1} WorkTime={rawWT:F1} " +
                            $"Strength={rawStr:F2} isPos={entry.IsPositive}");
                    }

                    string display = GetLocalizedEffectName(entry.EffectTypeName);
                    display = StripValueFromName(display);

                    // Skip blacklisted effects
                    if (IsBlacklisted(display) || IsBlacklisted(entry.EffectTypeName)) continue;

                    float time = PickTime(entry.TimeLeft, entry.WorkTime);
                    var de = new DisplayEffect
                    {
                        Name     = display,
                        Time     = time,
                        Strength = entry.Strength
                    };
                    if (entry.IsPositive)
                        AddDedup(_positiveEffects, seenPos, de, display);
                    else
                        AddDedup(_negativeEffects, seenNeg, de, display);
                    count++;
                }
            }
            catch { }
            return count;
        }

        // ================================================================
        //  IPlayerBuff HELPERS
        // ================================================================

        private bool IsIPB(object obj)
        {
            if (obj == null) return false;
            if (_ipbType != null && _ipbType.IsInstanceOfType(obj)) return true;
            return HasIPBProps(obj.GetType());
        }

        private static bool HasIPBProps(Type type)
        {
            if (type == null) return false;
            var f = BindingFlags.Public | BindingFlags.Instance;
            return type.GetProperty("BuffName", f) != null
                && type.GetProperty("Value", f)    != null
                && type.GetProperty("Active", f)   != null;
        }

        private string GetBuffKey(object buff)
        {
            try
            {
                string name = GetStringProp(buff, "BuffName");
                if (string.IsNullOrEmpty(name)) return null;
                string bodyPart = GetStringProp(buff, "BodyPart");
                float value = GetFloatProp(buff, "Value");
                // Use identity hash to ensure unique key for distinct objects
                int hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(buff);
                return $"{name}|{bodyPart}|{(value >= 0 ? "+" : "-")}|{hash}";
            }
            catch { return null; }
        }

        // ================================================================
        //  EFFECT EXTRACTION
        // ================================================================

        private EffectEntry ExtractEffectEntry(object effect)
        {
            try
            {
                var t = effect.GetType();
                float strength = GetFloatProp(effect, "Strength");
                if (strength == 0) strength = GetFloatProp(effect, "Value");
                return new EffectEntry
                {
                    EffectTypeName = GetCleanEffectName(t),
                    IsPositive     = IsPositiveEffect(t),
                    TimeLeft       = SanitizeTimer(GetFloatProp(effect, "TimeLeft")),
                    WorkTime       = SanitizeTimer(GetFloatProp(effect, "WorkTime")),
                    Strength       = strength
                };
            }
            catch { return null; }
        }

        private static bool IsUselessEffect(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string lower = name.ToLower();
            switch (lower)
            {
                case "stimulator": case "existence": case "medeffect":
                case "damagemodifier": case "breakpart": return true;
            }
            if (name.Length >= 16)
            {
                bool allHex = true;
                foreach (char c in name)
                    if (!((c >= '0' && c <= '9') ||
                          (c >= 'a' && c <= 'f') ||
                          (c >= 'A' && c <= 'F')))
                    { allHex = false; break; }
                if (allHex) return true;
            }
            if (name.StartsWith("_") || name.All(char.IsDigit)) return true;
            return false;
        }

        // ================================================================
        //  GUI
        // ================================================================

        private void OnGUI()
        {
            if (!_hudVisible || _localPlayer == null || _healthController == null) return;
            InitStyles();
            DrawHUD();
        }

        private int _lastFontSize;
        private float _lastBgAlpha;

        private Font _dynFont;

        private void InitStyles()
        {
            int fs = _fontSize.Value;
            float bgA = _backgroundAlpha.Value;
            if (_stylesInit && fs == _lastFontSize && Mathf.Approximately(bgA, _lastBgAlpha)) return;
            _stylesInit = true;
            _lastFontSize = fs;
            _lastBgAlpha = bgA;

            // Use a dynamic OS font so fontSize actually works
            // (the game's GUI.skin.label may have a bitmap font that ignores fontSize)
            if (_dynFont == null)
                _dynFont = Font.CreateDynamicFontFromOSFont("Arial", fs);

            var bg = MakeTex(2, 2, new Color(0.03f, 0.03f, 0.08f, bgA));

            _boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 6, 6) };
            _boxStyle.normal.background = bg;

            _titleStyle = new GUIStyle
            {
                font = _dynFont, fontSize = fs + 2, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _titleStyle.normal.textColor = new Color(0.4f, 0.85f, 1f);

            _colHeaderStyle = new GUIStyle
            {
                font = _dynFont, fontSize = fs, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _posStyle = new GUIStyle
            {
                font = _dynFont, fontSize = fs, wordWrap = false,
                clipping = TextClipping.Clip, richText = true
            };
            _posStyle.normal.textColor = new Color(0.3f, 1f, 0.3f);

            _negStyle = new GUIStyle
            {
                font = _dynFont, fontSize = fs, wordWrap = false,
                clipping = TextClipping.Clip, richText = true
            };
            _negStyle.normal.textColor = new Color(1f, 0.35f, 0.35f);

            _dimStyle = new GUIStyle
            {
                font = _dynFont, fontSize = fs, alignment = TextAnchor.MiddleCenter
            };
            _dimStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);

            _sepStyle = new GUIStyle();
            _sepStyle.normal.background = MakeTex(2, 2, new Color(0.3f, 0.3f, 0.45f, 0.6f));
        }

        private void DrawHUD()
        {
            if (_effectLayout.Value == EffectLayout.Stacked)
                DrawHUDStacked();
            else
                DrawHUDSideBySide();
        }

        /// <summary>Side-by-side two-column layout (positive left, negative right).</summary>
        private void DrawHUDSideBySide()
        {
            float x = _hudX.Value, y = _hudY.Value, w = _hudWidth.Value;
            int fs = _fontSize.Value;
            float spacing = _lineSpacing.Value;
            float lh = _posStyle.CalcSize(new GUIContent("Wg")).y + spacing;
            int posN = _positiveEffects.Count, negN = _negativeEffects.Count;

            bool showPos = posN > 0;
            bool showNeg = negN > 0;

            if (_hideEmptyColumns.Value && !showPos && !showNeg) return;

            bool twoColumns = _hideEmptyColumns.Value ? (showPos && showNeg) : true;
            int maxR = Mathf.Max(posN, negN);
            bool any = maxR > 0;

            float titleH = _showTitle.Value ? (fs + 2) + 10 : 0;
            float colH = _showColumnHeaders.Value ? fs + 8 : 0;
            float sepH = (_showTitle.Value || _showColumnHeaders.Value) ? 3 : 0;
            float rowsH = any ? maxR * lh : lh;
            float boxH = titleH + colH + sepH + rowsH + 20;
            float colW = twoColumns ? (w - 28) / 2f : (w - 20);

            GUILayout.BeginArea(new Rect(x, y, w, boxH), _boxStyle);

            if (_showTitle.Value)
            {
                GUILayout.Label("PLAYER BUFFS", _titleStyle);
                GUILayout.Space(1);
            }

            if (!any && !_hideEmptyColumns.Value)
            {
                GUILayout.Label("No active effects", _dimStyle);
            }
            else if (any)
            {
                if (_showColumnHeaders.Value)
                {
                    GUILayout.BeginHorizontal();
                    if (!_hideEmptyColumns.Value || showPos)
                    {
                        _colHeaderStyle.normal.textColor = new Color(0.3f, 1f, 0.3f);
                        GUILayout.Label($"POSITIVE ({posN})", _colHeaderStyle, GUILayout.Width(colW));
                    }
                    if (twoColumns) GUILayout.FlexibleSpace();
                    if (!_hideEmptyColumns.Value || showNeg)
                    {
                        _colHeaderStyle.normal.textColor = new Color(1f, 0.35f, 0.35f);
                        GUILayout.Label($"NEGATIVE ({negN})", _colHeaderStyle, GUILayout.Width(colW));
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Box("", _sepStyle, GUILayout.Height(1), GUILayout.ExpandWidth(true));
                    GUILayout.Space(1);
                }

                bool blinkVis = IsBlinkVisible();

                for (int i = 0; i < maxR; i++)
                {
                    GUILayout.BeginHorizontal();
                    if (!_hideEmptyColumns.Value || showPos)
                    {
                        if (i < posN)
                            DrawEffectLabel(_positiveEffects[i], _posStyle, colW, blinkVis, true);
                        else
                            GUILayout.Label("", _posStyle, GUILayout.Width(colW));
                    }
                    if (twoColumns) GUILayout.FlexibleSpace();
                    if (!_hideEmptyColumns.Value || showNeg)
                    {
                        if (i < negN)
                            DrawEffectLabel(_negativeEffects[i], _negStyle, colW, blinkVis, false);
                        else
                            GUILayout.Label("", _negStyle, GUILayout.Width(colW));
                    }
                    GUILayout.EndHorizontal();
                    if (spacing > 0f) GUILayout.Space(spacing);
                }
            }
            GUILayout.EndArea();
        }

        /// <summary>Stacked layout: positive effects on top, then negative below.</summary>
        private void DrawHUDStacked()
        {
            float x = _hudX.Value, y = _hudY.Value, w = _hudWidth.Value;
            int fs = _fontSize.Value;
            float spacing = _lineSpacing.Value;
            float lh = _posStyle.CalcSize(new GUIContent("Wg")).y + spacing;
            int posN = _positiveEffects.Count, negN = _negativeEffects.Count;

            bool showPos = posN > 0;
            bool showNeg = negN > 0;

            if (_hideEmptyColumns.Value && !showPos && !showNeg) return;

            float titleH = _showTitle.Value ? (fs + 2) + 10 : 0;
            float posHeaderH = (_showColumnHeaders.Value && showPos) ? fs + 10 : 0;
            float negHeaderH = (_showColumnHeaders.Value && showNeg) ? fs + 10 : 0;
            float sepH = (showPos && showNeg) ? 6 : 0;
            float posRowsH = showPos ? posN * lh : 0;
            float negRowsH = showNeg ? negN * lh : 0;
            bool any = showPos || showNeg;
            float emptyH = any ? 0 : lh;
            float boxH = titleH + posHeaderH + posRowsH + sepH + negHeaderH + negRowsH + emptyH + 20;
            float colW = w - 20;

            GUILayout.BeginArea(new Rect(x, y, w, boxH), _boxStyle);

            if (_showTitle.Value)
            {
                GUILayout.Label("PLAYER BUFFS", _titleStyle);
                GUILayout.Space(1);
            }

            if (!any && !_hideEmptyColumns.Value)
            {
                GUILayout.Label("No active effects", _dimStyle);
            }
            else
            {
                bool blinkVis = IsBlinkVisible();

                // Positive section
                if (showPos)
                {
                    if (_showColumnHeaders.Value)
                    {
                        _colHeaderStyle.normal.textColor = new Color(0.3f, 1f, 0.3f);
                        GUILayout.Label($"POSITIVE ({posN})", _colHeaderStyle);
                        GUILayout.Box("", _sepStyle, GUILayout.Height(1), GUILayout.ExpandWidth(true));
                        GUILayout.Space(1);
                    }
                    for (int i = 0; i < posN; i++)
                    {
                        DrawEffectLabel(_positiveEffects[i], _posStyle, colW, blinkVis, true);
                        if (spacing > 0f) GUILayout.Space(spacing);
                    }
                }

                // Separator between sections
                if (showPos && showNeg)
                    GUILayout.Space(sepH);

                // Negative section
                if (showNeg)
                {
                    if (_showColumnHeaders.Value)
                    {
                        _colHeaderStyle.normal.textColor = new Color(1f, 0.35f, 0.35f);
                        GUILayout.Label($"NEGATIVE ({negN})", _colHeaderStyle);
                        GUILayout.Box("", _sepStyle, GUILayout.Height(1), GUILayout.ExpandWidth(true));
                        GUILayout.Space(1);
                    }
                    for (int i = 0; i < negN; i++)
                    {
                        DrawEffectLabel(_negativeEffects[i], _negStyle, colW, blinkVis, false);
                        if (spacing > 0f) GUILayout.Space(spacing);
                    }
                }
            }
            GUILayout.EndArea();
        }

        /// <summary>Draw a single effect label with blink support and per-segment colours.</summary>
        private void DrawEffectLabel(DisplayEffect de, GUIStyle style, float colW, bool blinkVis, bool isPositive)
        {
            bool hide = ShouldBlink(de) && !blinkVis;
            GUILayout.Label(FmtLine(de, isPositive, hide), style, GUILayout.Width(colW));
        }

        /// <summary>Compare two DisplayEffect by time (ascending). Effects without time go to the bottom.</summary>
        private static int CompareByTime(DisplayEffect a, DisplayEffect b)
        {
            bool aHas = a.Time > 0;
            bool bHas = b.Time > 0;
            if (aHas && bHas) return a.Time.CompareTo(b.Time);
            if (aHas && !bHas) return -1;
            if (!aHas && bHas) return 1;
            return 0;
        }

        /// <summary>Abbreviate a buff name using the abbreviation dictionary.</summary>
        private string AbbreviateName(string name)
        {
            if (!_abbreviateNames.Value || string.IsNullOrEmpty(name)) return name;
            // Exact match first
            if (_abbreviations.TryGetValue(name, out string exact)) return exact;
            // Case-insensitive match
            foreach (var kv in _abbreviations)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            // Partial replacement: check if name contains a key as a word
            foreach (var kv in _abbreviationPartial)
            {
                if (name.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    name = Regex.Replace(name, Regex.Escape(kv.Key), kv.Value, RegexOptions.IgnoreCase);
            }
            return name;
        }

        private string FmtLine(DisplayEffect de, bool isPositive, bool blinkHide)
        {
            string alphaS = blinkHide ? "26" : "";
            string nameCol = GetEffectNameColor(de.Name, isPositive) + alphaS;
            string s = $"<color={nameCol}>{AbbreviateName(de.Name)}</color>";
            if (_showValues.Value && de.Strength != 0f)
            {
                string sign = de.Strength >= 0 ? "+" : "";
                string vCol = _valueColor.Value + alphaS;
                s += $" <color={vCol}>({sign}{de.Strength:G4})</color>";
            }
            if (_showTime.Value && de.Time > 0)
            {
                string tCol = (de.Time <= _timeColorThreshold.Value
                    ? _timeExpiringColor.Value : _timeNormalColor.Value) + alphaS;
                s += $"  <color={tCol}>{FmtTime(de.Time)}</color>";
            }
            return s;
        }

        /// <summary>Check if a display effect should be blinking (about to expire).</summary>
        private bool ShouldBlink(DisplayEffect de)
        {
            if (!_blinkEnabled.Value) return false;
            if (de.Time <= 0) return false; // no timer or permanent
            return de.Time <= _blinkThreshold.Value;
        }

        /// <summary>Returns true when the blink is in the "visible" phase.</summary>
        private bool IsBlinkVisible()
        {
            // sin wave: visible when > 0
            return Mathf.Sin(Time.unscaledTime * _blinkSpeed.Value * Mathf.PI * 2f) > 0f;
        }

        // ================================================================
        //  HELPERS
        // ================================================================

        private static void AddDedup(List<DisplayEffect> list, HashSet<string> seen,
                                     DisplayEffect de, string key)
        {
            // Check if an entry with the same display name already exists (cross-system merge)
            var ex = list.Find(e => e.Name == de.Name);
            if (ex != null)
            {
                // Keep the longest remaining time
                if (de.Time > ex.Time) ex.Time = de.Time;
                if (de.Strength != 0 && ex.Strength == 0) ex.Strength = de.Strength;
                seen.Add(key);
                return;
            }
            seen.Add(key);
            list.Add(de);
        }

        private static string StripValueFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int idx = name.LastIndexOf(" (");
            if (idx > 0 && name.EndsWith(")"))
            {
                string inner = name.Substring(idx + 2, name.Length - idx - 3)
                    .TrimEnd('%').TrimEnd('\u00B0').TrimEnd('C').TrimEnd(' ');
                if (inner.Length > 0)
                {
                    if (inner[0] == '+' || inner[0] == '-') inner = inner.Substring(1);
                    if (float.TryParse(inner, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                        return name.Substring(0, idx);
                }
            }
            return name;
        }

        private static float PickTime(float tl, float wt)
            => tl > 0 ? tl : (wt > 0 ? wt : 0);

        /// <summary>Parse numeric value from a name like "Выносливость (+30)" or "Температура тела (-0.1 C°)"</summary>
        private static float ParseValueFromName(string cleanName)
        {
            if (string.IsNullOrEmpty(cleanName)) return 0f;
            int idx = cleanName.LastIndexOf('(');
            if (idx < 0 || !cleanName.EndsWith(")")) return 0f;
            string inner = cleanName.Substring(idx + 1, cleanName.Length - idx - 2)
                .TrimEnd('%').TrimEnd('\u00B0').TrimEnd('C').TrimEnd(' ').Trim();
            if (inner.Length == 0) return 0f;
            if (float.TryParse(inner, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out float val))
                return val;
            return 0f;
        }

        /// <summary>Remove Unity rich-text color tags: &lt;color=#RRGGBBAA&gt;…&lt;/color&gt;</summary>
        private static string StripColorTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // <color=#XXXXXXXX> or <color=red> ... </color>
            s = Regex.Replace(s, @"<color=[^>]*>", "");
            s = s.Replace("</color>", "");
            return s.Trim();
        }

        private static float SanitizeTimer(float val)
        {
            if (float.IsNaN(val) || float.IsInfinity(val)) return -1f;
            if (val < -1f || val > 100000f) return -1f;
            return val;
        }

        private static float GetFloatProp(object obj, string name)
        {
            try
            {
                var type = obj.GetType();
                var f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var prop = type.GetProperty(name, f);
                if (prop != null) { var v = prop.GetValue(obj);
                    if (v is float fv) return fv; if (v is double dv) return (float)dv;
                    if (v is int iv) return iv; }
                var fld = type.GetField(name, f);
                if (fld != null) { var v = fld.GetValue(obj);
                    if (v is float fv) return fv; if (v is double dv) return (float)dv;
                    if (v is int iv) return iv; }
            }
            catch { }
            return 0f;
        }

        private static bool GetBoolProp(object obj, string name, bool def = false)
        {
            try
            {
                var type = obj.GetType();
                var f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var p = type.GetProperty(name, f);
                if (p != null) { var v = p.GetValue(obj); if (v is bool b) return b; }
                var fl = type.GetField(name, f);
                if (fl != null) { var v = fl.GetValue(obj); if (v is bool b) return b; }
            }
            catch { }
            return def;
        }

        private static string GetStringProp(object obj, string name)
        {
            try
            {
                var type = obj.GetType();
                var f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var p = type.GetProperty(name, f);
                if (p != null) return p.GetValue(obj)?.ToString();
                var fl = type.GetField(name, f);
                if (fl != null) return fl.GetValue(obj)?.ToString();
            }
            catch { }
            return null;
        }

        private string GetCleanEffectName(Type type)
        {
            string n = type.Name;
            if (n.Contains("+")) n = n.Split('+').Last();
            if (n.Contains("`")) n = n.Split('`')[0];
            return n;
        }

        private bool IsPositiveEffect(Type type)
        {
            string name = type.Name.ToLower();
            string[] posExact = {
                "painkiller","regeneration","healthregen","energyregen",
                "hydrationregen","staminaregen","antidote","skillrate",
                "maxstamina","weightlimit","damagereduction","berserk",
                "bodytemperature","surgery","quantumtunnelling","endurance"
            };
            foreach (var kw in posExact) if (name == kw) return true;
            string[] negKw = {
                "bleeding","heavybleeding","lightbleeding","fracture",
                "contusion","intoxication","lethalintoxication",
                "destruction","exhaustion","dehydration",
                "tremor","tunnelvision","pain","flash","stun",
                "disorientation","sidecontusion","musclepain",
                "mildmusclepain","severemusclepain","radexposure",
                "fatigue","chronicstaminafatigue"
            };
            foreach (var kw in negKw) if (name.Contains(kw)) return false;
            return true;
        }

        private string FmtTime(float seconds)
        {
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return "";
            if (seconds > 100000f) return "\u221E";
            switch (_timeFormat.Value)
            {
                case TimeFormat.SecondsOnly:
                    return $"{seconds:F0}s";
                case TimeFormat.MinutesOnly:
                {
                    int m = (int)(seconds / 60); int s = (int)(seconds % 60);
                    return $"{m}m {s:D2}s";
                }
                default: // Auto
                {
                    if (seconds < 60) return $"{seconds:F0}s";
                    int m = (int)(seconds / 60); int s = (int)(seconds % 60);
                    return $"{m}m {s:D2}s";
                }
            }
        }

        // ================================================================
        //  LOCALIZATION
        // ================================================================
        private string GetLocalizedEffectName(string effectType)
        {
            if (string.IsNullOrEmpty(effectType)) return "???";
            string gl = TryLocale(effectType);
            if (!string.IsNullOrEmpty(gl)) return gl;
            if (_fallback.TryGetValue(effectType, out string fb)) return fb;
            return effectType;
        }

        private string TryLocale(string key)
        {
            try
            {
                if (_localeDict == null && !_localeDictSearched) FindLocaleDict();
                if (_localeDict == null) return null;
                foreach (var tk in new[] { key, $"EffectType/{key}", key.ToLower(),
                    $"interface/health/effect/{key}", $"Buff/{key}", $"Skill/{key}" })
                    if (_localeDict.TryGetValue(tk, out string r) && !string.IsNullOrEmpty(r)) return r;
            }
            catch { }
            return null;
        }

        private Dictionary<string, string> _localeDict;
        private bool _localeDictSearched;
        private void FindLocaleDict()
        {
            _localeDictSearched = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.FullName.Contains("Assembly-CSharp")) continue;
                    foreach (var t in asm.GetTypes())
                    {
                        if (!t.Name.Contains("Locale") && !t.Name.Contains("locale")) continue;
                        foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (f.FieldType != typeof(Dictionary<string, string>)) continue;
                            var d = f.GetValue(null) as Dictionary<string, string>;
                            if (d != null && d.Count > 100)
                            {
                                _localeDict = d;
                                Log.LogInfo($"Locale dict: {t.Name}.{f.Name} ({d.Count} entries)");
                                return;
                            }
                        }
                    }
                    break;
                }
            }
            catch (Exception ex) { Log.LogWarning($"Locale: {ex.Message}"); }
        }

        private Texture2D MakeTex(int w, int h, Color c)
        {
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            var tex = new Texture2D(w, h);
            tex.SetPixels(px); tex.Apply(); return tex;
        }

        // ================================================================
        //  ABBREVIATION DICTIONARIES
        // ================================================================

        /// <summary>Exact name → abbreviated name.</summary>
        private static readonly Dictionary<string, string> _abbreviations =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"Health regeneration", "Health regen"},
            {"Stamina recovery", "Stamina rec"},
            {"Weight limit", "Weight lim"},
            {"Damage reduction", "Dmg reduce"},
            {"Quantum Tunnelling", "Quantum T."},
            {"Tunnel Vision", "Tunnel Vis"},
            {"Light Bleed", "Lt. Bleed"},
            {"Heavy Bleed", "Hv. Bleed"},
            {"Light Bleeding", "Lt. Bleed"},
            {"Heavy Bleeding", "Hv. Bleed"},
            {"Destroyed Part", "Destroyed"},
            {"Over-encumbered", "Over-enc."},
            {"Lethal Intox", "Leth. Intox"},
            {"LowEdgeHealth", "Low HP"},
            {"Disorientation", "Disorient."},
            {"Stamina Fatigue", "Stam. Fatigue"},
            {"Chronic Stamina Fatigue", "Chr. Stam. Fat."},
            {"Rad Exposure", "Rad Exp."},
            {"Body Temp", "Body T."},
            {"Skill Rate", "Skill Rt."},
            {"Max Stamina", "Max Stam."},
            {"Muscle Pain", "Musc. Pain"},
            {"Energy Regen", "Energy reg"},
            {"Hydration Regen", "Hydra. reg"},
            {"Stamina Regen", "Stam. reg"},
            {"Health Regen", "Health reg"},
        };

        /// <summary>Partial word replacements (applied via substring match).</summary>
        private static readonly Dictionary<string, string> _abbreviationPartial =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"regeneration", "regen"},
            {"recovery", "rec"},
            {"Perception", "Percep."},
            {"Metabolism", "Metabol."},
            {"Endurance", "Endur."},
            {"Attention", "Atten."},
            {"Strength", "Str."},
            {"Vitality", "Vital."},
            {"Painkiller", "Painkill."},
        };

        // ================================================================
        //  FALLBACK NAMES
        // ================================================================
        private static readonly Dictionary<string, string> _fallback =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"PainKiller","Painkiller"},{"Pain","Pain"},{"Tremor","Tremor"},
            {"TunnelVision","Tunnel Vision"},{"Contusion","Contusion"},
            {"Intoxication","Intoxication"},{"LethalIntoxication","Lethal Intox"},
            {"RadExposure","Rad Exposure"},{"BodyTemperature","Body Temp"},
            {"Berserk","Berserk"},{"Regeneration","Regeneration"},
            {"LightBleeding","Light Bleed"},{"HeavyBleeding","Heavy Bleed"},
            {"Fracture","Fracture"},{"DestroyedPart","Destroyed Part"},
            {"Exhaustion","Exhaustion"},{"Dehydration","Dehydration"},
            {"Encumbered","Encumbered"},{"OverEncumbered","Over-encumbered"},
            {"ChronicStaminaFatigue","Stamina Fatigue"},{"Fatigue","Fatigue"},
            {"HealthRegen","Health Regen"},{"EnergyRegen","Energy Regen"},
            {"HydrationRegen","Hydration Regen"},{"StaminaRegen","Stamina Regen"},
            {"SkillRate","Skill Rate"},{"MaxStamina","Max Stamina"},
            {"WeightLimit","Weight Limit"},{"DamageReduction","Dmg Reduction"},
            {"QuantumTunnelling","Quantum Tunnel"},{"Antidote","Antidote"},
            {"Bleeding","Bleeding"},{"Surgery","Surgery"},{"Endurance","Endurance"},
            {"Disorientation","Disorientation"},{"Stun","Stun"},{"Flash","Flash"},
            {"MusclePain","Muscle Pain"},
        };

        // ================================================================
        //  DATA CLASSES
        // ================================================================
        internal class EffectEntry
        {
            public string EffectTypeName;
            public bool IsPositive;
            public float TimeLeft = -1f, WorkTime = -1f;
            public float Strength;
        }

        internal class DisplayEffect
        {
            public string Name;
            public float Time;
            public float Strength;
        }
    }
}
