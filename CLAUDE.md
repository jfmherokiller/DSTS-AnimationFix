# AnimationFixes Working Context

## Goal

Replace missing player-specific motions on a swapped Digimon with a native idle or other safe motion,
so the model does not remain in a bind/T-pose. Keep all work and discoveries in this repository.

The model swap itself is already implemented in
`E:\ReverseEngineProjects\TimeStranger\DSTS-PlayerModelSwap`; do not recreate it here.

## Current result

`src/AnimationFix` is a standalone Reloaded II prototype. It hooks final named-animation playback and
calls the game's real lookup before changing anything. A real null result is considered only when the
active model has an enabled profile in `AnimationFallbacks.json`. Ordered rules classify missing
suffixes into roles, and ordered native candidate lists select the first resource the animator really
has loaded. Loaded base/patch/DLC resources remain authoritative.

Candidate values are not limited to known game suffixes. A profile can reference `custom_idle`,
`chr090_custom_idle.anim`, or a loader-relative path ending in that filename. The hook normalizes all
three to the suffix expected by the animator lookup. A future asset loader must still make the custom
motion available in that animator's resource list; the fallback mod intentionally does not pretend an
unloaded resource exists.

Existing-animation replacement is an advanced, double-gated path. Reloaded II's
`EnableAdvancedCharacterOverrides` must be true and the matched profile rule must declare
`overrideExisting: true`. The same setting unlocks a wildcard `"*"` profile. With the setting off,
valid native animations are always preserved and wildcard profiles are ignored. Profiles identify a
model, not one object instance, so an exact `chrNNN` profile affects every animator using that model.

This catches direct, blended, reset, and Lua/event motion paths without forcing a global idle or
needing a fragile cutscene-state flag. It does not modify valid native animations. Candidate choices
are cached by model and role so resource load order cannot make one model alternate between `fq01`
and `bn01` during the same session.

## Key static trace

- `RegisterLua_MotionBindings` `0x140A8FDC0`
- `Lua_Motion_PlayMotion` `0x140A690E0`
- `MotionController_PlayMotion` `0x1401B4290`
- `MotionController_PlayMotionBlend` `0x1401B4540`
- `MotionController_ResolveAndQueueMotion` `0x1401C1770`
- `MotionRequest_SetByName` `0x1401C27D0`
- `Animator_PlayMotionByName` `0x140269E80` — prototype hook target
- `Animator_FindMotionByName` `0x140268B90` — actual miss decision, called without detouring it

The higher resolver has an internal by-value request object in its final stack argument; Hex-Rays
currently reduces that object to `char`. Do not managed-hook it with the apparent decompiler
prototype. Playback has a verified simple five-argument ABI. The lookup has a simple three-argument
ABI and returns the resolved motion pointer or null; it is not hooked because other callers use it as
an existence probe.

See `docs/RE-NOTES.md` for signatures and detailed evidence.

## Build and distribution

`build.ps1` mirrors the Player Model Swap repository. It removes only this repository's `dist/`,
builds Release into `dist/timestranger.noah.animationfix/`, and creates
`dist/timestranger.noah.animationfix.zip` for Nexus Mods distribution.

## Data facts

- Animation assets are named `<model-or-skeleton>_<motion>.anim`.
- Digimon do have field motion sets; the old claim that their vocabulary was battle-only was wrong.
- The game explicitly preloads `fq01` during model resource preparation (`0x140AB8F12`).
- Guilmon also owns `fn02_01`, `fq02`, and `fe01`-`fe04`. Static callers use `fq02` as a temporary
  contextual motion, supporting its gesture role rather than neutral-idle use.
- `bn01` is the last-resort neutral/battle candidate.
- Requests where the suffix equals the model name, such as `chr090_chr090`, are base alias probes and
  are never remapped.
- `scripts/generate_motion_manifest.ps1` scans `app_0`, `patch`, and non-text `addcont_*` archives for
  research/coverage reporting. The runtime hook does not depend on the generated list.

## Related unresolved work

The missing-motion fallback does not solve exact-bone systems such as facial expression and look-at
constraints. The main model-swap mod already null-guards the known look-at crash at `0x140222560`.
True facial/bone retargeting remains a separate future problem.

## IDA safety

Use targeted decompilation and xrefs only; avoid whole-binary sweeps. Static image base is
`0x140000000`. Save the IDB after naming/comment changes. The IDB froze previously and lost eight
recent Analyze names; they were restored and saved on 2026-07-19.
