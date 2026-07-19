# DSTS Player Model Swap — Animation Fixes

Separate reverse-engineering and prototype repository for the remaining animation problems in
[DSTS-PlayerModelSwap](../DSTS-PlayerModelSwap/README.md).

The current prototype prevents one major source of bind/T-poses: when a configured swapped model is
asked to play a player-specific animation it does not own, the mod classifies the request and
substitutes an ordered, native candidate for that role. Existing native motions are left untouched.

## Current implementation

- Reloaded II project: `src/AnimationFix/`
- Playback hook: `Animator_PlayMotionByName` at static address `0x140269E80`
- Existence helper: `Animator_FindMotionByName` at `0x140268B90`
- User-editable profiles: `AnimationFallbacks.json`
- Default profile: Guilmon (`chr090`) only
- Guilmon role heuristic: `fn02_01`/`fq01` for neutral and movement placeholders,
  `fq02`/`fe01` for gestures, and `bn01` only as a last-resort neutral
- Rules use exact, prefix, suffix, contains, or catch-all matching and ordered candidate lists
- A selected candidate is cached by model and role, avoiding load-order-dependent alternation
- Research manifest: 15,044 archive-backed names generated from the installed base, patch, and add-on
  MVGL archives; runtime decisions use the game's real lookup result, not this static list
- Build status: v0.4.0 succeeds with zero warnings
- Runtime status: v0.1 produced useful missing-motion telemetry; the profile engine still needs
  broader in-game validation

The fallback is deliberately opt-in. If a model has no enabled profile, the request exists, no rule
matches, or no configured candidate is actually loaded, the original request is preserved. Base-name
alias probes such as `chr090_chr090` are always ignored.

An advanced Reloaded II setting can permit explicit JSON rules to replace animations that already
exist on any configured character model. This is double-gated: the setting must be enabled and the
individual rule must contain `"overrideExisting": true`. Wildcard `"*"` profiles are protected by the
same setting.

## Repository map

- `docs/RE-NOTES.md` — static trace, addresses, signatures, IDB repair log, and design rationale
- `docs/PROFILE-FORMAT.md` — profile schema and examples for augmenting more Digimon
- `docs/TESTING.md` — focused in-game validation checklist
- `src/AnimationFix/` — standalone Reloaded II mod
- `scripts/generate_motion_manifest.ps1` — regenerates native animation names from MVGL archives
- `data/` — older skeleton compatibility research
- `research/` — raw tables extracted while validating motion semantics

Build and create a Nexus-ready archive with:

```powershell
.\build.ps1
```

This mirrors the Player Model Swap build: it creates
`dist/timestranger.noah.animationfix/` and `dist/timestranger.noah.animationfix.zip`. The zip contains
the mod-ID folder expected when extracting into Reloaded II's `Mods` directory.

For a development deployment, set `RELOADEDIIMODS=C:\Reloaded-II\Mods` and build the project directly.
