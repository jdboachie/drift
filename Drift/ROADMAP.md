# Roadmap

What Drift still lacks compared with an everyday browser, roughly in order of how soon you'd
miss it. Items marked "check" haven't been confirmed in Drift.

## Done

- **Default browser**: registered with Windows (Settings › General › Make default); links from
  other apps open as tabs in the running Drift.
- **Ad blocking / extensions**: uBlock Origin Lite installed on first run; Settings › Extensions
  adds unpacked extensions.
- **Reopen closed tab** (Ctrl+Shift+T), remembered across restarts.
- **Tab switching shortcuts**: Ctrl+Tab / Ctrl+Shift+Tab (Arc's switcher, most recent first), Ctrl+1–9.
- **Command bar** (Arc's): Ctrl+T / Ctrl+L, with suggestions, open tabs, history and commands.
- **Zoom**: Chromium's own; the level shows in the address pill, and clicking it resets to 100%.
- **Split view** and **Peek**, as in Arc.
- **Mini player** (Arc's): a tab playing sound gets controls at the foot of the sidebar.
- **Integrations** (Arc's): pinned Gmail, Google Calendar and Outlook (mail and calendar) show unread
  email or today's meetings on hover; calendars count down to the next meeting, with Join and a reminder.
- **Downloads**: a sidebar row with progress, and a list (Ctrl+J) of current and past downloads.
- **Library** (Zen's): Downloads and History beside the tabs, swiped or clicked open.
- **Picture-in-picture** (Arc's): a playing video floats when you leave its tab, and comes back when you return.
- **History and clearing data**: Ctrl+H, in the library; Settings › Privacy clears history, cookies, cache,
  downloads and passwords by time range.
- **Saved passwords**: on by default (Settings › Privacy), with form filling.
- **Site info**: padlock or warning in the address pill; connection details, per-site
  permissions and clearing one site's data. Settings › Privacy lists every site's permissions.
- **Private window**: Ctrl+Shift+N.
- **Crashed pages**: a message and Reload (or Wait, for a hung page); recovers when the whole
  engine stops too.
- **Folders**, as Arc does them: nested, collapsible, draggable, between Essentials and pinned tabs.
  Not yet from Arc: a closed folder's preview on hover, folder icons (emoji), selecting several
  tabs to move or make a folder from at once, and Duplicate.

## Missing

| Feature | Notes |
| --- | --- |
| **Extension buttons** | Extensions run, but their toolbar buttons and popups have nowhere to show (e.g. uBlock's per-site switch). WebView2 has no API to open an extension's popup for a tab, so this would mean hosting the popup page ourselves, and most popups expect to know which tab they belong to. |
| **Managing saved passwords** | Passwords save and fill, but there's no list to view or delete them one by one (WebView2 has no API for it); Privacy can clear them all. |

## Later (Arc / Zen ideas)

- Spaces / workspaces
- More than one window (only a private one so far)

## Probably handled by WebView2 (check)

- Find in page (Ctrl+F); also "Find on Page" in the command bar
- Print (Ctrl+P); also "Print" in the command bar
- Back and forward with Alt+Left / Alt+Right
- Permission prompts (camera, microphone, location)

## Host the engine directly

Replace WinUI's WebView2 control with Drift's own host of the engine (CoreWebView2CompositionController),
porting WinUI's open-source WebView2 control. Gains: real zoom (ZoomFactor, per-site memory, exact
levels), shortcuts seen before the page (AcceleratorKeyPressed, no injected key script), explicit
suspension of background tabs. Costs: input forwarding (mouse, touch, pen, precision touchpad
scrolling and pinch), focus and IME, accessibility, drag and drop, DPI and window moves, all ours.
Staged: one tab behind a switch first, checked by hand for scrolling, gestures and typing, then
Peek, popups and the player.
