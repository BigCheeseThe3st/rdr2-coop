#!/usr/bin/env bash
# Push a RELEASED version's tree (tracked files only) to the public repo (the downloads repo, whose
# Releases hold the zips) as one new commit on its main. Never a test build: give the commit the
# release was built from. The private repo's history stays here: it carries the author's real name and
# e-mail. The public repo's own history is snapshot commits plus merged contributor PRs, so
# forks always have a real ancestor. Refuses when a private string is in the tree.
#   tools/publish-source.sh <commit>   # e.g. the commit that set VERSION to the released version
set -euo pipefail
REPO=${REPO:-BigCheeseThe3st/rdr2-coop}
REF=${1:?give the commit of the released version (never a test build)}
NAME=BigCheeseThe3st; MAIL=133584779+BigCheeseThe3st@users.noreply.github.com
# The private words to refuse live outside the repo (never published): one extended regex.
PRIVATE_FILE=${PRIVATE_FILE:-$HOME/.config/rdr2-coop/private-strings}
[ -s "$PRIVATE_FILE" ] || { echo "no $PRIVATE_FILE: can't check for private strings, not publishing" >&2; exit 1; }
PRIVATE=$(tr -d '
' < "$PRIVATE_FILE")
src=$(git rev-parse --show-toplevel)
ver=$(git -C "$src" show "$REF:VERSION" | tr -d '[:space:]')
gh release view "v$ver" -R "$REPO" >/dev/null 2>&1 || { echo "$ver has no release on $REPO: a test build? not publishing" >&2; exit 1; }
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

git clone -q "https://github.com/$REPO.git" "$work"
cd "$work"
git config credential.helper '!gh auth git-credential'
git config user.name "$NAME"; git config user.email "$MAIL"
find . -mindepth 1 -maxdepth 1 ! -name .git -exec rm -rf {} +
git -C "$src" archive "$REF" | tar -x -C "$work"
# shortcut: a fixed word list, extend it when a new private name appears
if grep -rIlE --exclude-dir=.git "$PRIVATE" . >/dev/null; then
    echo "private strings in the tree, not publishing:" >&2
    grep -rIlE --exclude-dir=.git "$PRIVATE" . >&2
    exit 1
fi
git add -A
if git diff --cached --quiet; then echo "public repo already matches $ver"; exit 0; fi
git commit -q -m "RDR2 Co-op $ver (source snapshot)"
git push -q origin HEAD:main
echo "published $ver to https://github.com/$REPO"
