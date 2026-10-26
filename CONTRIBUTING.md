# Contributing

This repository is a snapshot of the RDR2 Co-op source, pushed at each release
(one commit per snapshot, no history). Pull requests against it are welcome;
accepted ones are merged into the working tree and ship in the next release
with credit in the CHANGELOG.

## Before you start
- Say what you are doing in the Discord `#suggestions` forum first, so two
  people don't build the same thing. Built suggestions earn the Ideas Machine
  role; fixed bug reports the Bug Zapper role.
- Voice chat already exists: every dedicated server hands out a password-locked
  voice channel (see `#find-a-partner`). A second voice system is not wanted.
- Keep the player-facing text in the style of the existing messages ("Co-op:
  ...") and add a `CHANGELOG.md` line under the unreleased version.

## Building
`./build.sh` builds the mod (net48 via mono `csc`, net9 via `dotnet`), the
server and the tests. Run `tests/NetTests`, `api/CoreTests` and the relay
tests before a PR; `bash tools/check-shdn-newer.sh` checks the ScriptHookRDR2
.NET build still compiles.

## Licence
GPL-3.0 (see `LICENSE`): forks and modified copies must stay open under the
same licence and keep the attribution. Don't ship modified builds under the
original mod's name or update channel.

## Using an AI coding assistant (Antigravity, Cursor, Claude Code...)
Paste this as your first message, then name the issue you're taking:

```
You are helping me contribute to RDR2 Co-op, an open-source (GPL-3) story-mode co-op mod for
Red Dead Redemption 2. I am NOT the maintainer: all my work goes in as pull requests from my
fork, and the maintainer merges. Nothing I do may push to the upstream repo's main.

Setup
- Tooling: I need the .NET 9 SDK (not just the runtime). Check with `dotnet --list-sdks`; if
  there is no 9.x line, install it with `winget install Microsoft.DotNet.SDK.9` (or the x64 SDK
  installer from https://dotnet.microsoft.com/download/dotnet/9.0), reopen the terminal and
  check again. Git too (`git --version`; else `winget install Git.Git`). No Visual Studio,
  mono or zig needed.
- Upstream: https://github.com/BigCheeseThe3st/rdr2-coop. Fork it to my GitHub account,
  clone my fork, add upstream as a remote. Before every new piece of work:
  git fetch upstream && git checkout -b <issue-number>-<short-name> upstream/main
- Read README.md, BUILDING.md, CONTRIBUTING.md and CHANGELOG.md first and keep to them.
- The mod is C# in src/Net (pure network/protocol code, no game calls) and src/Mod
  (game-facing code). api/ is a port of ScriptHookRDR2DotNet; don't change it unless an issue
  needs a native that's missing. host/ (the .asi loader), the updater, release signing and the
  Cloudflare workers (rooms-server, bug-sync, list-server, log-server) are off limits for me.
- Build only the mod: dotnet build src/RDR2Coop.csproj -c Release -f net9.0
  Tests: dotnet build tests/NetTests.csproj -c Release && dotnet tests/bin/Release/net9.0/NetTests.dll
  All must pass before a PR. The full ./build.sh is Linux-only (zig + mono); I don't need it.
- To test in game, copy the built RDR2Coop.dll over the one in my game folder's RDR2Coop/
  directory. Never edit the game's installed files directly; always build from the branch.

Work
- One GitHub issue per branch and per PR. Pick from the open issues (issue #1 lists them
  easiest first). Comment on the issue that I'm taking it before starting.
- Smallest change that fully fixes the issue. No new features, options or refactors on the
  side. Match the surrounding code: a file header comment saying what the file is for and
  why, player-facing messages prefixed "Co-op: ", logging through CoopScript.Log in the
  existing wording ("what happened: why"). Every fix adds enough logging that a tester's
  scripts\RDR2Coop.log shows the fix working or not.
- Pure logic goes in src/Net with a test in tests/NetTests.cs (the existing Check(...)
  style); game calls stay in src/Mod.
- Add one line to CHANGELOG.md under the unreleased version at the top, in the existing voice.
- Don't add dependencies, don't add a second voice-chat system (servers already provide voice
  channels), don't touch update/signing code, don't rename or move files.

PR
- Push to my fork, open the PR against upstream main with: what was wrong, what the fix does,
  how I tested it in game (version, host or guest, what the log showed), and "Fixes #<n>".
- Keep the PR to that one issue. If review asks for changes, amend on the same branch.
```
