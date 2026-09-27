# fork-tools

This branch only holds automation for this fork of [GatherBuddy Reborn](https://github.com/FFXIV-CombatReborn/GatherBuddyReborn). It's the default branch because GitHub only runs scheduled workflows from there.

- `main` is an exact mirror of upstream's `main`.
- `feature/autogather-list-ipc` holds this fork's changes, rebased onto `main`.
- `.github/workflows/sync.yml` keeps both current daily and opens a "Fork sync failed" issue when a rebase conflicts or the build breaks.
