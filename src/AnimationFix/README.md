# Missing Animation Fallback

Experimental companion for Player Model Swap. When a configured model is asked to play an animation
file it does not own, this mod classifies the missing suffix and substitutes a native motion from its
user-editable profile instead of allowing the request to leave it in a bind/T-pose.

The bundled `AnimationFallbacks.json` enables Guilmon (`chr090`) only. Its default roles prefer:

1. `fn02_01`, then `fq01`, for idle and movement placeholders.
2. `fq02` or `fe01` for contextual gestures and cutscene placeholders.
3. `bn01` as a last-resort neutral battle idle.

Rules and candidates are ordered. The first candidate that the game's real lookup confirms is loaded
is cached by model and role for session-stable behavior. Existing native, patched, DLC, and
dynamically loaded motions are never changed. Unconfigured models, base-name alias probes, unmatched
rules, and roles with no loaded candidates preserve the original request.

Edit `AnimationFallbacks.json` in the installed mod folder to augment another Digimon, then restart
the game. JSON comments and trailing commas are accepted. See the repository's
`docs/PROFILE-FORMAT.md` for the complete schema and a copyable example.

Future custom animations are supported as profile candidates. These forms are equivalent:

```json
"idle": ["custom_idle", "chr090_custom_idle.anim", "some/loader/path/chr090_custom_idle.anim"]
```

The custom asset must target the active model and must be loaded into that animator's motion-resource
list by the game or an animation asset loader. If it is unavailable, the next candidate is tried.

## Advanced existing-animation overrides

By default, valid animations are never changed. To replace an existing animation on a configured
player, party member, NPC, or other character model:

1. Enable **Enable Advanced Character Overrides** in Reloaded II.
2. Add `"overrideExisting": true` to the matching rule in `AnimationFallbacks.json`.

The setting also unlocks a wildcard `"*"` profile. Exact model profiles take priority over the
wildcard. A profile is model-wide, so all active character instances using that model are affected.
Every replacement is still checked through the game's real lookup and is skipped when unavailable.
