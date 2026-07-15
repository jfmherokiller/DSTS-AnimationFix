# DSTS Player Model Swap — Animation Fixes

Follow-up work for the **Digimon Story: Time Stranger** player model swap mod, focused on the one
remaining issue: **player animations don't map cleanly onto swapped Digimon models** (T-poses in
cutscenes, the digivice menu, and any look-at/facial animation).

The model swap itself is done and shipping — see
`C:\Reloaded-II\Mods\timestranger.noah.playermodelswap` (source at
`E:\ReverseEngineProjects\TimeStranger\PlayerModelSwap`). This repo is only about animation.

## Start here
**Read `CLAUDE.md`** — it has the full context (how the swap works, what causes the T-pose, the
skeleton/`.nlst` mechanism, tools, key IDA addresses, and how to run the analysis) so a fresh session
can continue cold.

## Contents
- `CLAUDE.md` — full context + instructions.
- `data/PLAYER_ANIM_COMPATIBILITY.csv` — 532 Digimon ranked by humanoid-bone-role compatibility (tiers
  A–F). Already wired into the mod's dropdown as name prefixes.
- `data/skeleton_bone_overlap.csv` — exact bone-name overlap vs the player skeleton.
- `data/example_pc001a_player.nlst`, `data/example_chr050.nlst` — sample skeleton bone lists (player vs
  Agumon) showing the `name, hash, parenthash, joint` format and the naming-convention mismatch.
- `scripts/` — the analysis/generation scripts (`compat.py`, `compat2.py`, `gen_cs2.py`, `gen_config.py`).

## The core finding
Body animation retargets by bone **role** (humanoid Digimon work), but look-at/facial use **exact bone
names** (`head` vs `CP01`) no Digimon has. A real fix likely means a **bone remap/retarget** layer.
