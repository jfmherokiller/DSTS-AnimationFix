# Missing-animation fallback reverse-engineering notes

Date: 2026-07-19
Game image base: `0x140000000`

## IDB recovery

After the prior IDA freeze, these eight confirmed names had reverted to `sub_*` and were restored:

| Address | Restored name |
|---|---|
| `0x1409D4680` | `KeyHelp_ExpandLayout` |
| `0x140889910` | `UIKeyHelpFront_SetLayout` |
| `0x1406C5FB0` | `UIAnalyze_ConstructWidgets` |
| `0x140888DA0` | `UIKeyHelpFront_TickStateMachine` |
| `0x140A30620` | `FieldPlayerModelRefresh_ExecuteSequence` |
| `0x140C42690` | `FieldPlayerPreparation_RebuildModels` |
| `0x1401EEE10` | `FieldKeyHelp_UpdateLayout` |
| `0x1401FB090` | `Field_CanShowAnalyzeKeyHelp` |

The older model-resolution and Field-disable API names were audited and remained intact. Seventeen
motion functions, `Animator_PlayMotionByName`, and `Animator_FindMotionByName` were then named from
direct registration/call-path evidence. The IDB was saved successfully after both passes.

## Motion path

The Lua namespace registration at `RegisterLua_MotionBindings` (`0x140A8FDC0`) directly proves the
names of the Lua entry points. The ordinary path is:

```text
Lua Motion_PlayMotion
  -> Lua_Motion_PlayMotion                    0x140A690E0
  -> MotionController_PlayMotion              0x1401B4290
  -> MotionController_ResolveAndQueueMotion   0x1401C1770
  -> model-instance preload/request           0x140222C00
  -> Animator_PlayMotionByName                0x140269E80
  -> Animator_FindMotionByName                0x140268B90
```

Direct, blend, reset, and other native callers also converge on
`MotionController_ResolveAndQueueMotion`.

The controller and animator carry the active model/skeleton name and requested motion suffix down this
path. `Animator_FindMotionByName` explicitly copies the model string, appends `_`, then appends the
motion suffix before searching its resource list. A Guilmon cutscene request can therefore resolve as
`chr090_m010_190_c03` even though no such archive asset exists.

At `0x1401C1C9D`, the code optionally looks up `<model>_<motion>` in `field_dynamic_motion`, but a
failed lookup only clears optional metadata. The motion request still proceeds. At
`Animator_PlayMotionByName+0xBB` (`0x140269F3B`), the animator calls
`Animator_FindMotionByName`. A missing result does not cause the controller to choose another valid
animation. This is the bind/T-pose path addressed by the prototype.

## Why playback is hooked at `0x140269E80`

The first design targeted `MotionController_ResolveAndQueueMotion`, but static verification showed
that its final argument is an internal by-value request object. Hex-Rays reduces it to `char`; using
that apparent prototype in a managed detour could corrupt stack arguments.

`Animator_PlayMotionByName` has a safe five-argument ABI and represents an actual request to apply a
motion:

```cpp
void Animator_PlayMotionByName(
    Animator* animator,
    const char* motionName,
    float blendTime,
    int slot,
    bool looping);

MotionResource* Animator_FindMotionByName(
    std::string* modelName,
    const char* motionName,
    MotionResourceList* resources);
```

The lookup has 14 code xrefs, including callers that only test whether a motion exists. Detouring the
lookup and returning an idle on every miss would make those probes report a false success. The final
design therefore hooks playback only and resolves `Animator_FindMotionByName` as an unhooked helper.
It tests the requested resource, then profile candidates in their configured order, and passes the
first real name found to the original playback function. Valid base, patch, DLC, and dynamically
loaded resources remain authoritative, while unrelated existence probes keep their original
behavior.

Unique playback AOB signature:

```text
48 89 5C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 55 41 54 41 55 41 56 41 57 48 8B EC 48 83 EC 70 0F 29 74 24 ?? 45 8B E9
```

Unique lookup-helper AOB signature:

```text
40 53 55 56 57 41 56 48 83 EC 50 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? 49 8B F8
```

For reference, the higher resolver's unique signature is:

```text
48 8B C4 48 89 58 ?? 55 56 57 41 54 41 55 41 56 41 57 48 8D 68 ?? 48 81 EC 40 01 00 00 0F 29 70 ?? 0F 29 78 ?? 44 0F 29 40 ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 44 88 4C 24
```

## Fallback selection evidence

The old notes understated Digimon animation coverage. For example, Agumon owns field/event motions
including `chr050_fe01` through `fe04`, `chr050_fq01`, and `chr050_fq02`, in addition to battle
motions.

Model resource preparation at `0x140AB8F12` explicitly iterates a preload entry pointing to `fq01`.
That makes `fq01` a strong field-safe fallback candidate. `bn01` is a neutral battle idle. Attack
motions such as `ba01` are intentionally not forced.

Guilmon's archive-backed native set is:

```text
ba01 ba02 bd01 bd02 bd03 bf01 bg01 bg02 bn01 bn02 br01 bs01 bv01
f000 fe01 fe02 fe03 fe04 fn02_01 fq01 fq02
```

Two static call sites (`0x140B30482` and `0x140B30D3E`) play `fq02` temporarily and later reset or
stop the state. This supports treating `fq02` as a contextual gesture, not a neutral loop.
`fn02_01` does not occur as a literal in the executable, so its neutral classification is a naming
and inventory inference. Runtime testing remains authoritative.

The v0.1 runtime log exposed two design flaws in a global `fq01`/`bn01` fallback:

- Unrelated Digimon were remapped even though only the swapped player needed augmentation.
- Guilmon could select `bn01` first and `fq01` later as resources loaded, producing inconsistent
  behavior for equivalent misses.
- `chr090_chr090` is an expected base-name/alias probe, not a useful motion request.

Version 0.2 therefore uses opt-in profiles and caches a selected candidate by model and semantic
role. The research manifest contains 15,044 distinct `chr..._<motion>` names from base, patch, and
installed add-on archives, but runtime decisions do not depend on that static list. The hook behaves
as follows:

1. Before actual playback, run the game's lookup for the requested model/motion.
2. If it succeeds, call original playback with the exact original name.
3. If it fails, require an enabled exact model profile; unconfigured Digimon are untouched.
4. Ignore model-name alias probes and configured exact exclusions.
5. Evaluate profile rules in order to assign a role such as idle, movement, gesture, or cutscene.
6. Test that role's candidates in order using the same real resource list.
7. Cache the successful candidate for that model/role and call original playback with it.
8. If no candidate resolves, call original playback with the untouched missing request.

The shipped profile is Guilmon-only. Users can add opt-in profiles for other Digimon without
recompiling; see `PROFILE-FORMAT.md`. The hook cannot alter a valid native request. Runtime testing is
still required before folding it into the production model-swap mod.

## Future custom-animation compatibility

Profile candidates are arbitrary strings rather than a hardcoded motion enum. Version 0.3 also
normalizes suffixes, complete `<model>_<motion>.anim` filenames, and loader-relative paths ending in
such filenames. The final lookup still receives the suffix because
`Animator_FindMotionByName` constructs `<active-model>_<suffix>` internally.

This separates selection from asset delivery. If a future loader adds a compatible custom animation
to the active animator's resource list, the existing hook can select it immediately through JSON. If
the asset is absent or not registered, the real lookup returns null and the next candidate is tried;
no false resource success is introduced.

## Gated existing-animation replacement

Version 0.4 permits intentional replacement of resources that already resolve. It is double-gated:

- Reloaded configuration `EnableAdvancedCharacterOverrides` must be enabled.
- The first matching JSON rule must have `overrideExisting: true`.

The configuration additionally gates a `"*"` fallback profile. Exact model profiles win over the
wildcard. With advanced mode disabled, wildcard profiles are ignored and the lookup must return null
before any substitution occurs, preserving the original conservative behavior.

Scoping is model-wide because the playback hook observes the animator's model name, not a stable
gameplay owner identity. A profile for `chr090` therefore applies to every Guilmon animator that
reaches this playback function; it cannot select only one Guilmon NPC instance.

## Remaining animation problems

This fallback addresses missing animation resources. It does not synthesize bones or retarget tracks.
Exact-name features such as facial expressions and look-at constraints may still fail when a Digimon
skeleton lacks player bones. The production model-swap mod's look-at null guard remains necessary.
