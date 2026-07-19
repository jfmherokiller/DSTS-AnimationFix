using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using TimeStranger.AnimationFix.Configuration;
using TimeStranger.AnimationFix.Template.Configuration;
using CallingConventions = Reloaded.Hooks.Definitions.X64.CallingConventions;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace TimeStranger.AnimationFix;

public sealed class Mod : IMod
{
    // Animator_PlayMotionByName @ 0x140269E80. Detour only actual playback, not the many callers that
    // use Animator_FindMotionByName as an existence test.
    private const string PlayMotionByNameSig =
        "48 89 5C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 55 41 54 41 55 41 56 41 57 " +
        "48 8B EC 48 83 EC 70 0F 29 74 24 ?? 45 8B E9";

    // Animator_FindMotionByName @ 0x140268B90. Called as a helper to determine whether the requested
    // or fallback resource actually exists in this animator's loaded resource list.
    private const string FindMotionByNameSig =
        "40 53 55 56 57 41 56 48 83 EC 50 48 8B 05 ?? ?? ?? ?? 48 33 C4 " +
        "48 89 44 24 ?? 49 8B F8";

    private const int MaxNameLength = 160;

    [Function(CallingConventions.Microsoft)]
    private delegate nint FindMotionByNameFn(nint modelName, nint motionName, nint resources);

    [Function(CallingConventions.Microsoft)]
    private delegate void PlayMotionByNameFn(
        nint animator,
        nint motionName,
        float blendTime,
        int slot,
        byte looping);

    private ILogger _logger = null!;
    private IHook<PlayMotionByNameFn>? _motionHook;
    private FindMotionByNameFn? _findMotion;
    private IReadOnlyDictionary<string, ModelProfile> _profiles =
        new Dictionary<string, ModelProfile>(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _loggedEvents =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, nint> _motionNamePointers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _selectedFallbacks =
        new(StringComparer.OrdinalIgnoreCase);
    private Config _configuration = null!;
    private volatile bool _advancedCharacterOverrides;

    public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfig)
    {
        var loader = (IModLoader)loaderApi;
        var config = (IModConfig)modConfig;
        _logger = (ILogger)loader.GetLogger();

        var configurator = new Configurator(loader.GetModConfigDirectory(config.ModId));
        _configuration = configurator.GetConfiguration<Config>(0);
        _configuration.ConfigurationUpdated += OnConfigurationUpdated;
        ApplyConfiguration(_configuration, log: false);

        var profilePath = Path.Combine(loader.GetDirectoryForModId(config.ModId), "AnimationFallbacks.json");
        if (!LoadProfiles(profilePath))
        {
            _logger.WriteLine($"[{config.ModId}] No valid animation profiles loaded; fallback disabled.");
            return;
        }

        _logger.WriteLine(
            $"[{config.ModId}] Advanced character overrides: " +
            $"{(_advancedCharacterOverrides ? "ENABLED" : "disabled (missing motions only)")}.");

        var hooksController = loader.GetController<IReloadedHooks>();
        if (hooksController == null || !hooksController.TryGetTarget(out var hooks) || hooks == null)
        {
            _logger.WriteLine($"[{config.ModId}] Reloaded.Hooks is unavailable; fallback disabled.");
            return;
        }

        var scannerController = loader.GetController<IStartupScanner>();
        if (scannerController == null || !scannerController.TryGetTarget(out var scanner))
        {
            _logger.WriteLine($"[{config.ModId}] SigScan controller is unavailable; fallback disabled.");
            return;
        }

        scanner.AddMainModuleScan(FindMotionByNameSig, result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{config.ModId}] Motion lookup signature not found; fallback disabled.");
                return;
            }

            var baseAddress = Process.GetCurrentProcess().MainModule!.BaseAddress;
            var address = (long)baseAddress + result.Offset;
            _findMotion = Marshal.GetDelegateForFunctionPointer<FindMotionByNameFn>((nint)address);
            _logger.WriteLine($"[{config.ModId}] Resolved motion lookup @ 0x{address:X}.");
        });

        scanner.AddMainModuleScan(PlayMotionByNameSig, result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{config.ModId}] Motion playback signature not found; fallback disabled.");
                return;
            }

            var baseAddress = Process.GetCurrentProcess().MainModule!.BaseAddress;
            var address = (long)baseAddress + result.Offset;
            _motionHook = hooks.CreateHook<PlayMotionByNameFn>(PlayMotionByNameHook, address).Activate();
            _logger.WriteLine($"[{config.ModId}] Hooked missing-motion playback fallback @ 0x{address:X}.");
        });
    }

    private void PlayMotionByNameHook(
        nint animator,
        nint motionName,
        float blendTime,
        int slot,
        byte looping)
    {
        var replacement = motionName;

        try
        {
            var findMotion = _findMotion;
            if (findMotion != null && animator != 0)
            {
                var model = ReadStdString(animator + 16);
                var requested = ReadCString(motionName);

                if (!string.IsNullOrEmpty(model)
                    && !string.IsNullOrEmpty(requested)
                    && TryGetProfile(model, out var profile)
                    && profile.Enabled
                    && !string.Equals(model, requested, StringComparison.OrdinalIgnoreCase)
                    && !profile.IgnoreExact.Contains(requested, StringComparer.OrdinalIgnoreCase))
                {
                    var originalExists = findMotion(animator + 16, motionName, animator + 80) != 0;
                    var rule = profile.Rules.FirstOrDefault(x => x.Matches(requested));
                    if (rule == null
                        || !profile.Roles.TryGetValue(rule.Role, out var candidates)
                        || candidates == null)
                    {
                        if (!originalExists && profile.LogUnmatched) LogUnmatchedOnce(model, requested);
                    }
                    else if (!originalExists || (_advancedCharacterOverrides && rule.OverrideExisting))
                    {
                        var fallback = SelectFallback(
                            findMotion,
                            animator,
                            model,
                            requested,
                            rule.Role,
                            candidates);
                        if (fallback != null)
                        {
                            replacement = GetMotionNamePointer(fallback);
                            LogRemapOnce(model, requested, fallback, rule.Role, originalExists);
                        }
                    }
                }
            }
        }
        catch
        {
            // Preserve the exact original playback request if a model is rebuilding or unreadable.
            replacement = motionName;
        }

        _motionHook!.OriginalFunction(animator, replacement, blendTime, slot, looping);
    }

    private bool TryGetProfile(string model, out ModelProfile profile)
    {
        if (_profiles.TryGetValue(model, out profile!)) return true;
        if (_advancedCharacterOverrides && _profiles.TryGetValue("*", out profile!)) return true;
        profile = null!;
        return false;
    }

    private string? SelectFallback(
        FindMotionByNameFn findMotion,
        nint animator,
        string model,
        string requested,
        string role,
        IReadOnlyList<string> candidates)
    {
        var selectionKey = $"{model}|{role}";
        if (_selectedFallbacks.TryGetValue(selectionKey, out var selected))
        {
            var selectedPointer = GetMotionNamePointer(selected);
            if (findMotion(animator + 16, selectedPointer, animator + 80) != 0) return selected;
            _selectedFallbacks.TryRemove(selectionKey, out _);
        }

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeCandidate(model, candidate);
            if (string.IsNullOrEmpty(normalized)
                || string.Equals(normalized, requested, StringComparison.OrdinalIgnoreCase)) continue;

            var candidatePointer = GetMotionNamePointer(normalized);
            if (findMotion(animator + 16, candidatePointer, animator + 80) == 0) continue;

            _selectedFallbacks[selectionKey] = normalized;
            return normalized;
        }

        return null;
    }

    private static string? NormalizeCandidate(string model, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        // Profiles may use the suffix expected by the game lookup (custom_idle), an asset filename
        // (chr090_custom_idle.anim), or a loader-relative path ending in that filename. Keeping this
        // normalization here lets future custom-animation packs reference their actual asset names.
        var normalized = candidate.Trim().Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash >= 0) normalized = normalized[(lastSlash + 1)..];
        if (normalized.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^5];

        var modelPrefix = model + "_";
        if (normalized.StartsWith(modelPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[modelPrefix.Length..];

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private nint GetMotionNamePointer(string motionName) =>
        _motionNamePointers.GetOrAdd(motionName, static name => Marshal.StringToHGlobalAnsi(name));

    private void LogRemapOnce(string model, string requested, string fallback, string role, bool originalExisted)
    {
        var action = originalExisted ? "override" : "missing";
        var key = $"{action}:{model}_{requested}->{model}_{fallback}";
        if (_loggedEvents.TryAdd(key, 0) && _loggedEvents.Count <= 200)
        {
            _logger.WriteLine(
                $"[timestranger.noah.animationfix] " +
                $"{(originalExisted ? "Override" : "Missing")} {model}_{requested}.anim; " +
                $"role={role}, using {model}_{fallback}.anim.");
        }
    }

    private void OnConfigurationUpdated(IUpdatableConfigurable configurable)
    {
        _configuration = (Config)configurable;
        ApplyConfiguration(_configuration, log: true);
    }

    private void ApplyConfiguration(Config configuration, bool log)
    {
        _advancedCharacterOverrides = configuration.EnableAdvancedCharacterOverrides;
        if (log)
        {
            _logger.WriteLine(
                "[timestranger.noah.animationfix] Advanced character overrides " +
                $"{(_advancedCharacterOverrides ? "ENABLED" : "disabled")}." );
        }
    }

    private void LogUnmatchedOnce(string model, string requested)
    {
        var key = $"unmatched:{model}_{requested}";
        if (_loggedEvents.TryAdd(key, 0) && _loggedEvents.Count <= 200)
            _logger.WriteLine($"[timestranger.noah.animationfix] Missing {model}_{requested}.anim; no profile rule matched.");
    }

    private bool LoadProfiles(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _logger.WriteLine($"[timestranger.noah.animationfix] Profile file not found: {path}");
                return false;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            };
            var file = JsonSerializer.Deserialize<FallbackProfileFile>(File.ReadAllText(path), options);
            if (file == null || file.SchemaVersion != 1)
            {
                _logger.WriteLine("[timestranger.noah.animationfix] AnimationFallbacks.json has an unsupported schemaVersion.");
                return false;
            }

            var profiles = new Dictionary<string, ModelProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var (model, profile) in file.Profiles)
            {
                if (string.IsNullOrWhiteSpace(model) || profile == null) continue;
                profile.Normalize();
                profiles[model.Trim()] = profile;
            }

            _profiles = profiles;
            _logger.WriteLine(
                $"[timestranger.noah.animationfix] Loaded {_profiles.Count} animation profile(s) from {path}.");
            return _profiles.Count != 0;
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[timestranger.noah.animationfix] Failed to load AnimationFallbacks.json: {ex.Message}");
            return false;
        }
    }

    private sealed class FallbackProfileFile
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, ModelProfile> Profiles { get; set; } = new();
    }

    private sealed class ModelProfile
    {
        public string Description { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public bool LogUnmatched { get; set; }
        public List<string> IgnoreExact { get; set; } = new();
        public Dictionary<string, List<string>> Roles { get; set; } = new();
        public List<ProfileRule> Rules { get; set; } = new();

        public void Normalize()
        {
            IgnoreExact ??= new();
            Rules ??= new();
            Roles = Roles == null
                ? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, List<string>>(Roles, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class ProfileRule
    {
        public string Match { get; set; } = "exact";
        public string Pattern { get; set; } = "";
        public string Role { get; set; } = "fallback";
        public bool OverrideExisting { get; set; }

        public bool Matches(string value) => (Match ?? "").ToLowerInvariant() switch
        {
            "exact" => string.Equals(value, Pattern ?? "", StringComparison.OrdinalIgnoreCase),
            "prefix" => value.StartsWith(Pattern ?? "", StringComparison.OrdinalIgnoreCase),
            "suffix" => value.EndsWith(Pattern ?? "", StringComparison.OrdinalIgnoreCase),
            "contains" => value.Contains(Pattern ?? "", StringComparison.OrdinalIgnoreCase),
            "any" => true,
            _ => false,
        };
    }

    private static unsafe string? ReadStdString(nint value)
    {
        // MSVC std::string: inline/pointer union +0x00, size +0x10, capacity +0x18.
        if (value == 0 || IsBadReadPtr(value, 32u)) return null;
        var length = *(nuint*)(value + 16);
        var capacity = *(nuint*)(value + 24);
        if (length == 0 || length > MaxNameLength) return null;

        var data = capacity > 15 ? *(nint*)value : value;
        if (data == 0 || IsBadReadPtr(data, length)) return null;
        return Marshal.PtrToStringAnsi(data, checked((int)length));
    }

    private static string? ReadCString(nint value)
    {
        if (value == 0 || IsBadStringPtrA(value, MaxNameLength)) return null;
        return Marshal.PtrToStringAnsi(value);
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsBadReadPtr(nint pointer, nuint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsBadStringPtrA(nint pointer, nuint maximumLength);

    public void Suspend() { }
    public void Resume() { }
    public void Unload() { }
    public bool CanUnload() => false;
    public bool CanSuspend() => false;
    public Action Disposing => () => { };
}
