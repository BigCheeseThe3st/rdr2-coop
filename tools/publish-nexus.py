#!/usr/bin/env python3
"""Publishes a built release to the Nexus Mods page through the Nexus API (v3).

    tools/publish-nexus.py                  the co-op mod (main file), dry run
    tools/publish-nexus.py --go             ... for real
    tools/publish-nexus.py enrage-patch --go   the enRage patch (optional file)

For each product: uploads release/upload/<zip> (with its MD5), adds it as the
next version of the existing Nexus file, and posts that version's entry from
the changelog. Skips a version that's already on Nexus. The enRage patch is
never published unless named, since its file is kept archived until enRage's
author replies. Reads the API key from ~/.config/rdr2-coop/nexus-api-key and
never prints it. Standard library only.
"""
import base64
import hashlib
import json
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

API = "https://api.nexusmods.com/v3"
ROOT = Path(__file__).resolve().parent.parent
KEY_FILE = Path.home() / ".config/rdr2-coop/nexus-api-key"
MOD_ID = "12987981114342"   # reddeadredemption2 mod 11238

PRODUCTS = {
    "coop": dict(file_id="8217579", name="RDR2 Coop", category="main", version_file="VERSION",
                 changelog="CHANGELOG.md", zip="RDR2-Coop-{v}.zip", mod_version=True),
    "enrage-patch": dict(file_id="8217585", name="RDR2 Coop EnRage Patch", category="optional",
                         version_file="ENRAGE_PATCH_VERSION", changelog="CHANGELOG-ENRAGE-PATCH.md",
                         zip="RDR2-Coop-enRage-Patch-{v}.zip", mod_version=False),
}


def key():
    if KEY_FILE.stat().st_mode & 0o077:
        sys.exit(f"{KEY_FILE} must be readable by you only: chmod 600 {KEY_FILE}")
    return KEY_FILE.read_text().strip()


def call(method, path, body=None):
    req = urllib.request.Request(API + path, method=method, data=None if body is None else json.dumps(body).encode(),
                                 headers={"apikey": key(), "Accept": "application/json",
                                          **({"Content-Type": "application/json"} if body is not None else {})})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            text = r.read().decode()
            return json.loads(text) if text else {}
    except urllib.error.HTTPError as e:
        sys.exit(f"Nexus API {method} {path}: HTTP {e.code}: {e.read().decode(errors='replace')[:600]}")


def changelog_entry(path, version):
    """The text under '## <version> ...' up to the next '## '."""
    text = (ROOT / path).read_text()
    m = re.search(rf"^## {re.escape(version)}\b[^\n]*\n(.*?)(?=^## |\Z)", text, re.M | re.S)
    if not m:
        sys.exit(f"{path} has no '## {version}' entry")
    return m.group(1).strip()


def upload(zip_path):
    data = zip_path.read_bytes()
    md5 = hashlib.md5(data)
    up = call("POST", "/uploads", {"size_bytes": len(data), "filename": zip_path.name, "md5": md5.hexdigest()})["data"]
    req = urllib.request.Request(up["presigned_url"], method="PUT", data=data, headers={
        "Content-Disposition": f'attachment; filename="{zip_path.name}"',
        "Content-MD5": base64.b64encode(md5.digest()).decode(),
        "Content-Type": "application/octet-stream"})
    with urllib.request.urlopen(req, timeout=300):
        pass
    call("POST", f"/uploads/{up['id']}/finalise")
    for _ in range(60):   # Nexus checks the archive before it can be used
        if call("GET", f"/uploads/{up['id']}")["data"]["state"] == "available":
            return up["id"]
        time.sleep(5)
    sys.exit("the upload didn't become available within 5 minutes; check the mod page")


def publish(product, go):
    p = PRODUCTS[product]
    version = (ROOT / p["version_file"]).read_text().strip()
    zip_path = ROOT / "release/upload" / p["zip"].format(v=version)
    if not zip_path.is_file():
        sys.exit(f"{zip_path} isn't built: run tools/make-release.sh first")
    notes = changelog_entry(p["changelog"], version)
    if product != "coop":   # Nexus keeps one changelog per version for the whole mod page
        notes = p["name"] + ":\n" + notes
    versions = call("GET", f"/mod-files/{p['file_id']}/versions")["data"]
    versions = versions.get("versions", versions) if isinstance(versions, dict) else versions
    if any(v.get("version") == version for v in versions):
        print(f"{product} {version} is already on Nexus: nothing to do")
        return
    latest = versions[0] if versions else None
    print(f"{product}: {zip_path.name} -> file {p['file_id']} as version {version} ({p['category']})"
          f"{', replacing ' + latest['version'] if latest else ''}")
    print("changelog:\n  " + notes.replace("\n", "\n  "))
    if not go:
        print("(dry run: add --go to publish)")
        return
    upload_id = upload(zip_path)
    call("POST", f"/mod-files/{p['file_id']}/versions", {
        "upload_id": upload_id, "name": p["name"], "version": version, "file_category": p["category"],
        "update_mod_version": p["mod_version"], "previous_version_id": latest["id"] if latest else None})
    call("POST", f"/mods/{MOD_ID}/changelogs", {"version": version, "changelog": notes})
    print(f"published {product} {version}")


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    for product in args or ["coop"]:
        if product not in PRODUCTS:
            sys.exit(f"unknown product {product}: one of {', '.join(PRODUCTS)}")
        publish(product, "--go" in sys.argv)
