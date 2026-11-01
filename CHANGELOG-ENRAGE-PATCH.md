# Unofficial enRage co-op patch - changelog

For enRage 0.9.6 (Nexus mod 10967). Needs RDR2 Co-op; host only. For enRage 0.9.5.2, use patch 0.4.3; for 0.9.5.1, patch 0.4.1.

## 0.5.0 (30 Oct 2026) - for enRage 0.9.6; needs RDR2 Co-op 0.7.66-rc.2 or newer
- Works with enRage 0.9.6 (life stories, townsfolk you can talk to,
  families, newspapers, translations). Everything the co-op patch did for
  0.9.5.2 is carried over: your partners' kills and damage count as yours,
  grudges are per player (a nemesis hunts the partner he hates most), news
  and grudge notices reach the right player, ambushes plan for a far partner,
  and partners and the copies of the host's people are never named or turned
  into nemeses.
- The new 0.9.6 parts follow the same rule: a townsperson killed by your
  partner counts as killed by you (their family hears), and your partners'
  characters don't get names, life stories or families of their own.

## 0.4.3 (27 Oct 2026) - for enRage 0.9.5.2; needs RDR2 Co-op 0.7.62 or 0.9.0-beta.8.4 or newer for the full fix
- Pressing Insert (reload) no longer leaves the old enRage running in the
  background with the mod's own loader: enRage's file-saving thread now ends
  when enRage stops (anything still being saved is finished first).
- No more "two copies of enRage" warning after Insert with the mod's own loader:
  the copy check no longer counts the copy left behind by the reload (on modern
  .NET it can stay in memory, because .NET's shared array pool keeps a hook to
  enRage's own types). A real second copy in scripts\ is still reported.

## 0.4.2 (24 Oct 2026) - for enRage 0.9.5.2; needs RDR2 Co-op 0.4.0 or newer (tested with 0.7.2)
- Works with enRage 0.9.5.2, the current version on Nexus (0.4.1 needs 0.9.5.1).
- The same co-op changes as 0.4.1, applied to 0.9.5.2: partners' kills and wounds
  count as yours, per-player grudges, ambushes on far-away partners, nemesis names
  and F11 lists for partners.

## 0.4.1 (23 Oct 2026) - needs RDR2 Co-op 0.4.0 or newer (tested with 0.6.0)
- No change to the patch itself (the patched enRage is byte-identical to 0.4.0).
- Tested with RDR2 Co-op 0.6.0 (server built into the host's game, server browser).
- The readme covers both installers: the Windows RDR2CoopSetup.exe and the Linux
  setup.py (both in the GitHub downloads), since the patch has to be applied to
  your own enRage file.

## 0.4.0 (23 Oct 2026) - needs RDR2 Co-op 0.4.0
- A partner's non-fatal hits (shots, punches, knives) start grudges like
  yours: the co-op mod now says which weapon each relayed hit came from.
- Names of nemeses and people you've met show over them on partners' screens.

## 0.3.0 (23 Oct 2026) - needs RDR2 Co-op 0.3.0
- Far-away partners get ambushed too: a nemesis who hates a partner most comes
  for them when they're over 300 m from the host (enRage's own ambush interval
  and chance), staged in the partner's game. Killing him there counts with
  enRage's rules: he can cheat death and carry their scar. Experimental.
- When enRage's men regroup or rejoin a fight, they go after the player their
  nemesis hates most, not always the host.

## 0.2.0 (23 Oct 2026) - needs RDR2 Co-op 0.2.0
- Per-player grudges: each nemesis remembers which co-op player earned his
  hate (saved with enRage's own files and rolled back with its save points).
- An ambushing nemesis and his crew go after the player he hates most, when
  that player is within 250 m of the host.
- Grudge notices go to the player who caused them (shown on the host's screen
  with their name); death and world news go to every partner.
- Partners can list their nemeses (F11 in RDR2 Co-op).

## 0.1.0 (23 Oct 2026) - needs RDR2 Co-op 0.1.0
First release.
- Partners' kills, wounds and knockouts count as the player's own.
- Co-op characters and copied NPCs are never named, scanned or made into nemeses.
