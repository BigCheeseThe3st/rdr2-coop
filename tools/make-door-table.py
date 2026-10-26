#!/usr/bin/env python3
# Writes src/Net/DoorTable.cs, the list of RDR2's doors the door sync uses (src/Mod/DoorSync.cs):
# each door's hash (what the game's door system knows it by) and where it stands. RDR2 can't list
# its doors, so the list comes from femga's rdr3_discoveries (doorHashes/doorhashes.lua), a dump of
# the game's own door data. Run it again to pick up a newer version of that file (change SHA).
import re
import urllib.request

SHA = "2760d9e1fda6e5530d7264ddbde3c77ed786cc63"
URL = f"https://raw.githubusercontent.com/femga/rdr3_discoveries/{SHA}/doorHashes/doorhashes.lua"
ROW = re.compile(r'\[(\d+)\]\s*=\s*\{\s*\d+\s*,\s*-?\d+\s*,\s*"[^"]*"\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\}')

text = urllib.request.urlopen(URL).read().decode()
doors = {}
for m in ROW.finditer(text):
    h = int(m[1])
    doors[h - (1 << 32) if h >= 1 << 31 else h] = [round(float(v) * 10) for v in m.groups()[1:]]   # a door listed twice: the last one, as in Lua
assert len(doors) > 1500, f"only {len(doors)} doors found: has the file's layout changed?"

with open("src/Net/DoorTable.cs", "w") as f:
    f.write(f"""// RDR2's doors as its door system knows them, nearly all of them (made by tools/make-door-table.py; don't
// edit by hand): each door's hash, as the game's scripts write it, and where it stands, in decimetres (x, y, z).
// From femga's rdr3_discoveries, doorHashes/doorhashes.lua at {SHA[:12]}, a dump of the game's door data.
namespace RDR2Coop.Net
{{
    static class DoorTable
    {{
        public static readonly int[] Raw =
        {{
""")
    for h, (x, y, z) in doors.items():
        f.write(f"            {h}, {x}, {y}, {z},\n")
    f.write("        };\n    }\n}\n")
print(f"src/Net/DoorTable.cs: {len(doors)} doors")
