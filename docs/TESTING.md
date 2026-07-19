# Runtime validation checklist

The prototype is built and deployed at:

```text
C:\Reloaded-II\Mods\timestranger.noah.animationfix
```

The v0.1 global fallback has been run and produced the telemetry that motivated the profile engine.
The current DLL compiles cleanly but has not yet completed this checklist.

## First smoke test

1. Enable both `timestranger.noah.playermodelswap` and `timestranger.noah.animationfix` in Reloaded II.
2. Start with Guilmon (`A_Guilmon`, id 90), which owns both `chr090_fq01` and `chr090_bn01`.
3. Confirm the Reloaded log reports one loaded profile, then contains both `Resolved motion lookup`
   and `Hooked missing-motion playback fallback`.
4. Load into a field and stand idle, walk, run, and stop several times.
5. Open and close the Digivice menu.
6. Trigger a short cutscene or scripted player gesture that previously caused a T-pose.

Expected result: valid Guilmon motions continue normally. Missing requests are logged once with a
role, for example `role=movement, using chr090_fn02_01.anim`, and Guilmon uses a native placeholder
instead of entering a bind/T-pose.

Specific checks from the v0.1 log:

- `chr090_chr090` produces no remap log and remains untouched.
- `chr090_fn01_01` uses the first loaded candidate in the `idle` role.
- `chr090_fw01_01` and `chr090_fw01_01_end01` use the same cached `movement` candidate.
- `chr090_m120_030_c07`, `c08`, and `c09` use the same cached `cutscene` candidate.
- Unconfigured models such as `chr391`, `chr151`, and `chr343` produce no remap logs.

## Regression checks

- Set Player Digimon to `None`: human animations must be unchanged and no fallback log should appear.
- Use Temporary-Human mode: human motions must remain unchanged.
- Return to Guilmon: fallback should resume without a map reload beyond whatever the model swap needs.
- Enter and leave battle, then return to the field.
- Copy the Guilmon profile to a second model key, adjust its candidates, restart the game, and confirm
  only that newly configured model is augmented.
- Add a nonexistent `chr090_test_custom.anim` as the first idle candidate and verify the next native
  candidate is selected without a crash. Once a custom asset loader exists, repeat with a real loaded
  asset and verify the custom candidate wins.

## Advanced override checks

1. Add an exact rule for a known existing motion with `"overrideExisting": true`.
2. Leave **Enable Advanced Character Overrides** off and confirm the native motion is unchanged.
3. Turn the setting on and confirm the log says `Override ...; role=..., using ...`.
4. Turn the setting off and confirm subsequent playback returns to the native motion.
5. Add a `"*"` profile and confirm it is completely inactive while the setting is off.
6. With the setting on, test an exact profile and wildcard simultaneously; the exact profile must win.
7. If two character instances use the same model, confirm both are affected and document that
   model-wide scope for the profile author.
- Watch for repeated animation restarts, sliding, camera timing problems, or a request that should have
  remained a valid native motion.

## Failure handling

If the game crashes or animation playback becomes unstable, disable only
`timestranger.noah.animationfix`, reproduce with the model-swap mod alone, and preserve the Reloaded
log. The most useful evidence is the last `Missing ...; role=..., using ...` line before the problem,
together with the profile used for that model.

If the hook signature is not found, do not attempt an address patch. Re-scan the current executable
and update both stable signatures corresponding to static `0x140269E80` and `0x140268B90` in this game
version.
