import csv, re, sys, io

SRC = r"E:\ReverseEngineProjects\TimeStranger\dump\digimon_status.digimon_status_data.csv"
MOD = r"C:\Reloaded-II\Mods\timestranger.noah.mods"
AP_CSV = MOD + r"\mvgl-loader\app_0\data\player_model.mbe\player_change_model.ap.csv"
CFG    = MOD + r"\ReMIX\Config\config.yaml"

# player_change_model schema (17 cols): Int, String2(name), String2(modelRef), Empty, Int,
# 11x Bool, String2(attach). Mirror the working Agumon row's bool pattern.
BOOLS = "false,false,false,true,false,false,false,false,false,true,false"
HEADER = "Int 1,String2 2,String2 3,Empty 4,Int 5,Bool 6,Bool 7,Bool 8,Bool 9,Bool 10,Bool 11,Bool 12,Bool 13,Bool 14,Bool 15,Bool 16,String2 17"
KEY_BASE = 90000

def pretty(char_name):
    # char_EX-TYRANOMON -> Ex-Tyranomon ; char_AGUMON_KIZUNA -> Agumon Kizuna
    n = char_name[5:] if char_name.startswith("char_") else char_name
    parts = n.replace("_", " ").split(" ")
    out = []
    for p in parts:
        out.append("-".join(seg.capitalize() for seg in p.split("-")))
    return " ".join(w for w in out if w)

rows = []
with open(SRC, encoding="utf-8", newline="") as f:
    r = csv.reader(f)
    next(r)  # header
    for rec in r:
        if len(rec) < 4:
            continue
        rid, name, code = rec[0].strip(), rec[2].strip(), rec[3].strip()
        if not rid.isdigit():
            continue
        i = int(rid)
        if not name.startswith("char_"):
            continue
        if not re.fullmatch(r"chr\d{3,}", code):
            continue
        rows.append((i, name, code))

# unique by id, sorted -> keys 90000+id stay ascending (all > stock 11841) for binary search
rows = sorted({i: (i, n, c) for i, n, c in rows}.values())

# --- player_change_model.ap.csv ---
lines = [HEADER]
for i, name, code in rows:
    lines.append(f"{KEY_BASE + i},{name},{code},,1,{BOOLS},")
with open(AP_CSV, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(lines) + "\n")

# --- config.yaml ---
# choices unique display strings "Name (id)"; regex %((%d+)%) extracts id, +90000 in lua
seen, choices = set(), []
default = None
for i, name, code in rows:
    label = f"{pretty(name)} ({i})"
    if label in seen:
        label = f"{pretty(name)} #{i} ({i})"
    seen.add(label)
    choices.append(label)
    if i == 50:
        default = label

# "None" option (id 0 -> key 90000 sentinel) clears the change-model so the save is safe to
# remove the mod from. Default to None so a fresh install never dirties a save until opted in.
NONE = "None - Restore Original (0)"
choices = [NONE] + choices

y = []
y.append("settings:")
y.append("  - id: PlayerDigimon")
y.append("    name: Player Digimon")
y.append("    description: The Digimon model the field player is swapped to. Choose 'None - Restore Original' and walk through a loading zone (then save) BEFORE disabling the mod, or the saved player becomes an invisible null model.")
y.append("    type: enum")
y.append(f"    default: {NONE}")
y.append("    choices:")
for c in choices:
    y.append(f"      - {c}")
y.append("")
y.append("actions:")
y.append("  - using: RemixToolkit.Interfaces.IFileSystem")
y.append("    run: WriteFile")
y.append("    with:")
y.append("      - '{ModDir}/dsts-loader/patch/lua/player_model_config.lua'")
y.append("      - |")
y.append('        PLAYER_MODEL_SWAP_ID = 90000 + tonumber(string.match("{PlayerDigimon}", "%((%d+)%)"))')
import os
os.makedirs(os.path.dirname(CFG), exist_ok=True)
with open(CFG, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(y) + "\n")

print(f"digimon rows: {len(rows)}")
print(f"id range: {rows[0][0]}..{rows[-1][0]}  -> key range {KEY_BASE+rows[0][0]}..{KEY_BASE+rows[-1][0]}")
print(f"default: {default}")
print(f"wrote:\n  {AP_CSV}\n  {CFG}")
