# Animation fallback profile format

`AnimationFallbacks.json` is loaded once when the mod starts. It is intentionally opt-in: only exact
model keys listed under `profiles` can be augmented. Restart the game after editing it.

Motion values may use any of these equivalent forms:

```text
fn02_01
chr090_fn02_01.anim
some/loader/path/chr090_fn02_01.anim
```

The runtime strips directories, `.anim`, and the active model prefix before calling the game's
lookup. This permits a future custom-animation pack to use its actual asset filenames in the profile.

## Profile fields

| Field | Purpose |
|---|---|
| `enabled` | Set to `false` to preserve a profile without activating it. |
| `description` | Free-form notes for the profile author. |
| `logUnmatched` | Log a missing request when no rule matches it. |
| `ignoreExact` | Requested suffixes that must never be replaced. |
| `roles` | Named, ordered lists of native candidate suffixes. |
| `rules` | Ordered classifiers that map a missing suffix to a role. First match wins. |

Each rule may also contain `overrideExisting`. Its default is `false`. When it is `true`, the rule is
allowed to replace a motion that already exists only if **Enable Advanced Character Overrides** is
also enabled in Reloaded II.

Supported `match` values are `exact`, `prefix`, `suffix`, `contains`, and `any`. Matching is
case-insensitive. Put the catch-all `any` rule last.

The engine always performs these safety checks:

1. Preserve a request that already resolves.
2. Preserve a request when its suffix equals the active model name, such as `chr090_chr090`.
3. Test each candidate with the game's actual loaded-resource lookup.
4. Preserve the missing request if no candidate really exists.
5. Cache a successful candidate by model and role for stable behavior during the session.

## Overriding existing animations on any character

This capability has two independent safety locks:

1. Enable **Enable Advanced Character Overrides** in this mod's Reloaded II configuration.
2. Add `"overrideExisting": true` to the specific JSON rule that is allowed to replace a valid
   animation.

For example, this exact-model profile rule replaces Guilmon's existing `fq01` with a loaded custom
idle while advanced mode is enabled:

```json
"roles": {
  "customIdle": ["chr090_custom_idle.anim", "fn02_01"]
},
"rules": [
  {
    "match": "exact",
    "pattern": "fq01",
    "role": "customIdle",
    "overrideExisting": true
  }
]
```

Any exact model code can have a profile, not just the swapped player. A `chr343` profile applies to
every active animator using Gomamon's model. The hook cannot distinguish one party/NPC instance from
another instance using the same model code.

Advanced mode also unlocks a profile named `"*"`. It is used only when no exact model profile exists,
and it is completely ignored while the setting is disabled. This can apply one rule set across many
characters, but every replacement candidate still has to exist for the active model:

```json
"*": {
  "description": "Advanced wildcard example",
  "enabled": true,
  "logUnmatched": false,
  "ignoreExact": [],
  "roles": {
    "customIdle": ["custom_idle"]
  },
  "rules": [
    {
      "match": "exact",
      "pattern": "fq01",
      "role": "customIdle",
      "overrideExisting": true
    }
  ]
}
```

Do not mark a catch-all `any` rule with `overrideExisting` unless replacing nearly every animation is
truly intended. Turning the Reloaded setting off immediately restores safe missing-motion-only
behavior; JSON profile edits still require a game restart.

## Custom animations

Profiles accept arbitrary candidate names; there is no compiled allow-list. Put a custom candidate
before native fallbacks to prefer it when available:

```json
"idle": [
  "chr090_custom_idle.anim",
  "chr090_fn02_01.anim",
  "chr090_fq01.anim"
]
```

This mod chooses motions but does not load new `.anim` assets by itself. A future animation pack or
loader must provide a model-compatible asset and register/load it into the animator's motion-resource
list. The game's real lookup then sees it and the profile can select it. Until that happens, the
candidate safely fails and the next native fallback is used. A candidate that becomes available later
can still be selected because misses are not cached.

## Adding another Digimon

Copy the Guilmon object, change the key to the model prefix, and replace every candidate with motions
that model actually owns. For Gomamon, the key is `chr343`:

```json
"chr343": {
  "description": "Gomamon user profile",
  "enabled": true,
  "logUnmatched": true,
  "ignoreExact": ["chr343"],
  "roles": {
    "idle": ["fn02_01", "fq01", "bn01"],
    "movement": ["fn02_01", "fq01", "bn01"],
    "gesture": ["fq02", "fe01", "fq01", "bn01"],
    "cutscene": ["fe01", "fq02", "fq01", "bn01"],
    "fallback": ["fn02_01", "fq01", "bn01"]
  },
  "rules": [
    { "match": "exact",  "pattern": "fn01_01", "role": "idle" },
    { "match": "prefix", "pattern": "fw",      "role": "movement" },
    { "match": "prefix", "pattern": "fr",      "role": "movement" },
    { "match": "prefix", "pattern": "m",       "role": "cutscene" },
    { "match": "prefix", "pattern": "head_",   "role": "gesture" },
    { "match": "any",    "pattern": "*",       "role": "fallback" }
  ]
}
```

This example is a template, not proof that Gomamon owns every listed suffix. Remove nonexistent or
poor-looking candidates after checking the generated native-motion manifest and testing in game.
Choosing an idle for a missing walk/run request can cause sliding, but it is safer than a bind pose;
profiles can be refined later if a better locomotion motion is identified.

## Guilmon heuristic

The bundled Guilmon profile is based on archive inventory and targeted static analysis:

- `fn02_01`: inferred native neutral candidate.
- `fq01`: field-safe idle candidate explicitly included in model preparation.
- `fq02`: temporary contextual motion at known native call sites, used for gesture-like misses.
- `fe01`: field/event motion candidate for scripted gestures.
- `bn01`: neutral battle idle used only as a last resort.

These are semantic fallbacks, not animation retargeting. A profile can prevent a T-pose but cannot
make a Digimon skeleton perform a human animation it does not own.
