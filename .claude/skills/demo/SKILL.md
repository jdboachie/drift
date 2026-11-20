---
name: demo
description: Build, launch and drive Drift's demo copy, a separate Drift on its own profile, to see a change working without touching the user's own running Drift. Use whenever a change needs checking in the real app, a screenshot, or the user asks to "load the demo", "rebuild the demo" or preview something.
---

# Drift's demo copy

The demo is a second Drift, built from the working tree into its own folder and run on its own profile.
Test there, never in the user's Drift: they're usually using theirs, and its bin is locked while it runs.

Everything lives in `%LOCALAPPDATA%\DriftDemo`: `app\` (the build), `home\` (its profile, which keeps tabs and settings between runs) and `demo.pid`.
The scripts are in `scripts/` next to this file; run them with `powershell -NoProfile -File <script>`.

## Build and launch

```powershell
& .claude\skills\demo\scripts\build.ps1          # stop the demo, build into app\, relaunch, restore the registry
& .claude\skills\demo\scripts\build.ps1 -Seed    # same, with a fresh session (5 tabs, 3 in a split)
& .claude\skills\demo\scripts\build.ps1 -NoRun   # build only
& .claude\skills\demo\scripts\run.ps1            # relaunch without building
& .claude\skills\demo\scripts\stop.ps1
```

`build.ps1` prints `build succeeded` or `BUILD FAILED` with the first errors.
The window is centred on the first non-primary screen (the user's laptop screen), or pass `-Screen n` to `run.ps1`.

## Rules that keep the user's Drift safe

- **Never build into the repo's `bin`** while the user's Drift runs, and never restart theirs without telling them first. Overriding `OutDir` alone isn't enough: the XAML compiler copies `.xbf` files to `OutputPath`, so `build.ps1` sets both and warns if anything still lands in the user's bin. (Until 2026-11-20 it set only `OutDir`, which left new XAML beside the user's older `Drift.dll`.) The demo is yours: rebuild and restart it freely, even while they look at it.
- **Restore the registry after every launch.** Any Drift.exe that starts points the link handler at itself, so links from other apps would open the demo. `build.ps1` does this; after `run.ps1`, wait about 6 seconds and run `restore-registry.ps1`. It should report `0` values left.
- **`USERPROFILE` and `LOCALAPPDATA` both point at `home\`** (`run.ps1` sets them). With only one set, the demo finds the user's real profile and breaks it.
- **Stop only the demo.** `stop.ps1` and `run.ps1` check the saved process is `Drift.exe` under `app\`, since process IDs get reused.

## Driving it

Use UI Automation or the web engine's debugging port. Never send real keyboard input, and never use the mouse without the user's yes, asked at the end of a reply.

- `node scripts/press.mjs <key> [ctrl] [shift] [alt]` presses a key in the front page through the debugging port (9444), so Drift's shortcuts work: `press.mjs comma ctrl` opens Settings, `press.mjs d ctrl` bookmarks the page (the toast shows for a few seconds, so capture straight after).
- `settings.ps1 -Page Tabs -Out shot.png` selects a Settings page; `-Invoke 'Change…'` presses a button by its automation name. Both capture the window afterwards.
- `capture.ps1 -Out shot.png [-Title Settings]` captures one demo window's own pixels (PrintWindow), so the terminal or other windows on top don't show. Save captures to the scratchpad, then Read them.
- For anything else, find elements with UI Automation by `ProcessId` (from `demo.pid`) and `Name`, and use their patterns (Invoke, SelectionItem, Toggle, Value). Walk the tree with `TreeWalker.ControlViewWalker`: `FindFirst`/`FindAll` with `Descendants` don't reach into the window's XAML content.

## Checking that a change broke nothing

For refactors, compare the demo before and after on the same fixed session:

1. Save a session that shows what the change touches (pinned tabs, a split, an open folder…) and launch with `run.ps1 -Session file.json` (or `build.ps1 -Session`), before and after.
2. `uia-dump.ps1 -Out before.txt` lists every native element with its type, name and rectangle; diff the before and after dumps. Zero differences means the same elements in the same places with the same names.
3. `capture.ps1`, then `compare.ps1 -Before a.png -After b.png -Region x,y,w,h -Diff diff.png`. It compares edges, not raw colours, since the acrylic backdrop shows what's behind the window and changes between captures. Translucent strokes (dividers, card borders) can still differ when the backdrop does; read the diff image.
4. Drive the changed behaviour with UI Automation (open, press, close) and diff the before and after output.

Hover states need the mouse, so ask before checking them.

Hover cards (folders, Gmail, calendars) need a real hover, which needs the mouse, so ask first.
