# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Goal

Fix the **animation problem** for the Digimon Story: Time Stranger *player model swap* mod: when the
player character's model is swapped to a Digimon, player-specific animations don't map onto the Digimon
skeleton, causing **T-poses** (bind pose) — most visibly in cutscenes, the digivice menu, and any
look-at/facial animation. This repo is a follow-up effort dedicated to that animation issue; the model
swap itself already works.

## Background: the model swap mod (already working — do NOT re-solve)

The swap ships as a **native Reloaded II C# mod**:
- Source: `E:\ReverseEngineProjects\TimeStranger\PlayerModelSwap`
- Installed/built to: `C:\Reloaded-II\Mods\timestranger.noah.playermodelswap` (ModDll
  `TimeStranger.PlayerModelSwap.dll`)
- Read its `README.md` there for the full design.

How the swap works (context you'll need): it hooks **`FieldPlayer_ResolveModelRef`** (`0x1409ADEE0`)
and, per resolve, transiently sets the game's own change-model fields on the FieldPlayerSystem
(`+480` = key `90000+digimonId`, `+477` = flag), calls the original so the game fills the model
string+ref consistently from the `player_change_model` MBE table, then restores the fields. This is
**save-safe** (flag never persists) and **HUD-safe**. The `player_change_model` rows for all 582 Digimon
are shipped in the mod's `mvgl-loader/` and loaded by `MVGL.FileLoader.Reloaded`. A second hook
null-guards a cutscene look-at blend (`sub_140222560`) that crashed when a Digimon rig lacks the
player's `head` aim bone. Digimon model code = `chr` + zero-padded id (Agumon 50 → `chr050`).

Full RE notes for the swap live in `E:\ReverseEngineProjects\TimeStranger\CLAUDE.md`.

## The animation problem (what we learned)

- The game applies the **player's humanoid animation rig** to whatever model sits in the player slot.
- Body animation **retargets by bone ROLE**, so structurally-humanoid Digimon (Agumon, Guilmon, Leomon…)
  animate acceptably. Non-humanoid Digimon (quadrupeds, blobs, flyers) T-pose broadly.
- Some features use **exact bone names**, not roles: the look-at/aim constraint targets a bone literally
  named `head` (player) vs `CP01` (Digimon, from `model_setting.mbe` col53/col80), and facial expression
  bones the Digimon simply don't have. These **fail for ALL Digimon** regardless of humanoid-ness.
- Animations are per-skeleton `.anim` files named `<skeleton>_<animname>.anim` (e.g. `pc001a_e003.anim`,
  `chr050_ba01.anim`). The player and Digimon have largely **disjoint animation vocabularies** (player:
  `e0xx` expressions, `fg0x`/`head_*` gestures, `m0xx_yyy` cutscene motions, `r<NNN>` ride sets; Digimon:
  `chrNNN_ba/bd/bf/bg` battle anims).
- Skeleton bone lists are the **`.nlst` files** — plaintext, one bone per line: `name, hash, parenthash,
  joint`. Player `pc001a.nlst` = 112 bones with human naming (`head`, `l_arm`, `r_hand`, `l_knee`).
  Digimon use `J_` naming (`J_arm_r`, `J_head`). See `data/example_*.nlst`.

### Compatibility tiers (already computed)
`data/PLAYER_ANIM_COMPATIBILITY.csv` ranks 532 Digimon by humanoid-bone-role completeness (15 roles:
head/neck/spine + L/R arm/elbow/hand/leg/knee/foot). Tiers A(15/15) B(12-14) C(9-11) D(5-8) F(<5). The
mod's dropdown is already prefixed with these grades (`A_Agumon`, `F_Airdramon`). Regenerate the mod's
enum with `scripts/gen_cs2.py`.

## Approaches considered (and their ceilings)
- **Force player anims onto Digimon** — infeasible; the game already does this and it's the T-pose.
- **Force idle** — no clean chokepoint; would need cutscene detection (unsolved: no readable
  "is-event-playing" flag found) or would freeze the model in the field too.
- Likely real fixes to explore here: **bone retargeting / remap** (map player bone names → Digimon
  equivalents, e.g. `head`→`J_head`, `CP01`→player head, per-skeleton), or supplying/substituting the
  missing bones. Add the follow-up plan under "Ideas" below.

## Tools & environment
- **IDA Pro** with the game exe loaded, reachable via the `ida-pro-mcp` MCP tools. Imagebase
  `0x140000000` (may be rebased if a debugger is attached — convert with the live base). Confirm with
  `server_health` first.
- **MbeDumper** — `E:\ReverseEngineProjects\TimeStranger\MbeDumper` (.NET 9). Modes:
  `list <mvgl> [substr]`, `dump <mvgl> <mbeSubstr> <outDir>` (MBE→CSV), `extract <mvgl> <substr> <outDir>`,
  `extractregex <mvgl> <regex> <outDir>` (used to bulk-pull `.nlst`), `testap` (verify MBE CSV appends).
  Build: `dotnet build MbeDumper/MbeDumper.csproj -c Release`.
- **Game archives**: `E:\SteamLibrary\steamapps\common\Digimon Story Time Stranger\gamedata\`. Base data
  = `app_0.dx11.mvgl`; `patch.dx11.mvgl` overlays it (patch wins). `addcont_*` = DLC.
- **unluac** (`E:\ReverseEngineProjects\TimeStranger\tools\unluac.jar`) for decompiling the game's Lua
  5.2 bytecode; validate Lua edits with the `luaparser` pip package.
- Reloaded C# hooks: sig-scan (`IStartupScanner.AddMainModuleScan`) + `IReloadedHooks.CreateHook`.

## Key addresses / functions
- `FieldPlayer_ResolveModelRef` `0x1409ADEE0` — player model resolve (hooked by the swap).
- Look-at/aim blend `sub_140222560` (faults at `0x140222589`, `movss [rbx+1B8h]`, rbx=null v6) —
  null-guarded by the swap; this is the `head`-bone constraint.
- Motion system (Lua-bound in `sub_140A8FDC0`): `Motion_PlayMotion` = `sub_140A690E0` →
  `sub_1401B4290` → `sub_1401C1770` (plays by index/ref); also `Motion_ChangeExpression`,
  `Motion_AimOnlyThisFrame`, `Motion_PlayMotionBlend`.
- `model_setting.mbe` col53/col80 = aim/head bone name (player `head`, Digimon `CP01`).

## Scripts (in `scripts/`, Windows absolute paths inside — adjust as needed)
- `compat.py` — exact bone-NAME overlap vs player (proves only human models match by name).
- `compat2.py` — humanoid-ROLE scoring (the real ranking → `PLAYER_ANIM_COMPATIBILITY.csv`).
- `gen_cs2.py` — regenerate the mod's `Digimon.g.cs` enum with tier prefixes.
- `gen_config.py` — (from the old ReMIX mod) generate `player_change_model` CSV rows.
They read extracted CSV/nlst from `E:\ReverseEngineProjects\TimeStranger\dump\`; re-extract with
MbeDumper if that folder is gone (e.g. `extractregex app_0.dx11.mvgl "^(chr[0-9]+|pc001a)\.nlst$" out`).

## Ideas (fill in the follow-up plan)
> Placeholder for the animation-fix approach to pursue here (e.g. bone-name remap table, skeleton
> substitution, per-Digimon anim overrides). Add design + steps as you go.
