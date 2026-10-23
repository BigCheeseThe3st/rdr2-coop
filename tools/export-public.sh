#!/bin/sh
# Copies the committed source into the public repository (~/rdr2-coop-public by
# default), which has its own history and author, so nothing from this repo's
# history (or its author email) is published. Then commit + push there.
#   PUBLIC_NAME="name" PUBLIC_EMAIL="id+name@users.noreply.github.com" tools/export-public.sh "message"
set -e
cd "$(dirname "$0")/.."
OUT=${PUBLIC_DIR:-$HOME/rdr2-coop-public}
: "${PUBLIC_NAME:?set PUBLIC_NAME}" "${PUBLIC_EMAIL:?set PUBLIC_EMAIL}"
[ -d "$OUT/.git" ] || git init -q -b main "$OUT"
find "$OUT" -mindepth 1 -maxdepth 1 ! -name .git -exec rm -rf {} +
git archive HEAD | tar -x -C "$OUT"
# Never publish anything that still names the private setup.
if grep -rIl -i -E "beeseland|192\.168\.|ddns\.net|/home/george|gtrader|rtrader" "$OUT" --exclude-dir=.git --exclude=export-public.sh; then
  echo "personal details found in the files above: not committed"; exit 1
fi
cd "$OUT"
git add -A
git -c user.name="$PUBLIC_NAME" -c user.email="$PUBLIC_EMAIL" commit -q -m "${1:-Update}" && git log --oneline -1
