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

## Bugs

- **Shift+F10 on a tab row opens the wrong menu.** With a tab's row focused, Shift+F10 (or the Menu
  key) opens the sidebar's own menu (New tab, New folder, New live folder) instead of the tab's. A
  right-click opens the tab's menu as it should. The keyboard's request starts at the focused list
  item and goes up to the sidebar, while the tab's menu is on the row's content, inside the item.

## Later (Arc / Zen ideas)

- Spaces / workspaces
- More than one window (only a private one so far). Scope still to agree. The code is partly ready:
  a private window is already a second `MainWindow`, and the sidebar's row controls find their own
  window (`MainWindow.Of`).
- **Meet's own picture-in-picture**, as in Dia. In Drift, Meet sees the engine as "Microsoft Edge
  WebView2", never offers its picture-in-picture, and its menu's Open picture-in-picture floats a bare
  video. In Dia it opens Meet's own window (300×489, with a bar: back to the tab, the site, ×).
  Presenting Drift as Chrome to Meet stopped the bare-video fallback, but no window opened. Plan:
  present as Chrome to Meet, find out why its window doesn't open, match Dia's window (its size and
  bar), then do the same for Zoom.
- **Tooltip arrows**: Drift's tooltips with a small arrow pointing at what's hovered, like WinUI's
  TeachingTip's tail (a TeachingTip itself is too heavy for hover hints). Each tooltip would open on a
  fixed side, so its arrow points the right way. Still to decide: arrows on every tooltip, or only on
  the icon-only buttons.
- **Vertical split view** (Arc's top and bottom panes): one page over the other, as well as side by
  side. Made from the page's buttons ("Add bottom split") and the command bar ("Add Bottom Split");
  a divider dragged up and down (each page at least 160 px tall, double-click for half and half);
  swapping, showing one alone and closing animating up and down; kept with the session. Its divider's
  grip has to sit clear of the lower page's dots, which are in the middle of the gap. Still to work
  out: dragging the dots to swap stacked pages, dropping a tab on the top or bottom half of the page,
  Peek opening into a lower page, and switching a split between side by side and stacked.
  With it, Zen's grid for four pages (two over two). Splits of up to four side by side are done.

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
