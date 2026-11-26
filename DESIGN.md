# Drift design rules

Rules learned from building Drift, each with why. Read before building or changing UI.

## Components

- **Build from the shared pieces; never copy one.** Before writing a panel, row, card or style, look for it here:
  - `Views/`: the window's regions (caption bar, toolbar, address bar, command bar, switcher, Library, downloads peek, mini player, meeting notice, crash card, drag ghosts). A new region is a view of its own, with an `Owner` the window sets; its handlers stay in the window's code.
  - `Controls/`: what's used in more than one place. A tab's row (`TabRowView`), an Essential's tile, a folder's header, a page's card (`PageCardView`, also Peek's), a Profile's sidebar (`ProfilePageView`), handle and divider, settings rows and groups (`SettingsRow` in a `SettingsGroup`), a setting chosen by pictures (`PictureChoice`, as Dia shows its layouts; its pictures drawn with `PictureTabStyle` and `PictureGroupStyle`), `Toast`, `HoverCard`. A control in a list's row finds its window with `MainWindow.Of(this)`.
  - `Styles/`: colours (`Colors.xaml`, a light and a dark value for each), text, buttons, settings, the sidebar's rows, and floating surfaces (`FloatingPanelStyle`: the hairline, fill and shadow every panel over the window has).

  A list's template in `MainWindow.xaml` is one element; a row with more is a control. A shadow comes from `FloatingPanelStyle`, `PageControlsPillStyle` or `PageCardView`, never one made on the spot. A colour that has to be written out (a button's `ButtonBackground` and the like, which won't take an alias) is one of Drift's own, in the same theme. Checked by `Drift.Tests/ComponentRulesTests.cs`.
  Why: the same panel, row and colours were written out again and again, and drifted apart; one place for each keeps them alike and `MainWindow.xaml` readable (it had grown to 2,546 lines).
  <!-- 2026-11-20: "i want us to componentize: resuable components for consistency and reducing repetition. also, to help break up the monstrous mainwindow.xaml" -->

## Surfaces

- **When building from an Arc, Dia or Zen reference, copy its layout and behaviour, never its icons or artwork.**
  Icons come from Drift's own set: Segoe Fluent glyphs (`SymbolThemeFontFamily`), or Drift's Ionicons font (`Ui.IconFont`).
  Why: Drift has its own visual identity; copied icons clash with it and aren't ours to use.
  <!-- 2026-11-17: "oh no i didn't want you to copy the icons?? use our own icons please" -->

- **An icon's everyday meaning must match its action. Never reuse a glyph that already means something else.**
  + means add; a box with an arrow means share or pop out. (Replace page uses a pencil, E70F.)
  Why: people read the icon before the tooltip, so a wrong one misleads.
  <!-- 2026-11-17: "the replace page icon looks like a share or pop out icon" · "replace page with a plus icon?? nah figure out something else" -->

- **Anything grouping tabs in the sidebar (tab groups, splits) uses one card: `DriftFolderCardBrush`, a `DriftFolderCardBorderBrush` hairline, corner radius 10.** Tab groups are what folders were, designed exactly as they were ("tab groups are designed the exact same way as folders"), pinned with the pinned tabs or unpinned above the others ("they behave like folders except you can have them unpinned").
  A folder's card shows only while it's open; closed, it's a plain row. A split's shows always, in front or not, one under another or side by side, since it's what makes the pages read as one tab (the user, 2026-11-24, of side by side without it: "the horizontal split view tab doesn't have the folder background").
  Why: one card style makes groups read alike; a card on a closed group is noise.
  <!-- 2026-11-17: "the split view should have the same card treatment as the folders - just the color and border" · "the split row when it isn't open shouldn't have that folder-card treatment" -->
- **A split's pages sit one under another in the sidebar, each a full tab row (38 tall), the left page on top, as Search Browser's do.**
  Settings › Tabs › Split View tabs, a choice by pictures: Vertical (this, the default) or Horizontal, side by side in one row as they are on screen, each filling its share (the layout before 2026-11-24). Both stay: it's a setting, not a choice made for you. Settings words are plain: the options say "Vertical" and "Horizontal", not sentences.
  <!-- 2026-11-24: "the split view thing should be a setting" · "use "horizontal" and "vertical". be simple silly" -->
  <!-- 2026-11-24: "lets make split tabs's tab vertical instead of horizontal (like in the searchbrowser screenshot)" · asked whether a split in the background keeps its card: "it should get the box" -->

- **Essentials tiles are 48 tall with the folder cards' corner radius, 10.** Their placeholder, drop zone and drag ghost match.
- **Every row of Essentials fills the sidebar's width.** A short last row shares it too, so its tiles come out wider (five across three: three, then two at half the width each), never a row with a gap at its end. A tile moving between rows springs to its new width as well as its place.
  Why: the sidebar's big shapes should share one roundness.
  <!-- 2026-11-23: "the essentials should be as rounded as the folder cards" · "and slightly bigger" -->

- **In a group, the item in front is marked with the normal selected-tab card (`SplitFrontCardStyle`: `DriftSelectedBrush`, `DriftSelectedEdgeBrush`, radius 8) on the group's card. Never a faint tint.**
  Why: you must be able to tell at a glance which one is active.
  <!-- 2026-11-17: "i can't tell which split is active by looking at the tab. look at how arc (or zen) does it" -->

- **The integrations' hover cards (mail, calendar) are Arc's since its Sept 2023 redesign: no header, the content from edge to edge, and + floating round over the bottom-right corner (`HoverCard`).**
  Emails: sender and age, the subject on one line, the start of it on up to two, a hairline between each two. Meetings: the hour grid, each meeting only its name, on a tint of the accent.
  Why: the card had copied Arc's 2022 one (a tinted header with the site's icon, a title and a square +), which Arc had since replaced.
  <!-- 2026-11-23: "it seems our integration's popover may be based on arc's old version. can you confirm by researching what the latest version looked like? then make ours conform." -->

- **The next meeting is a row at the foot of the sidebar, above the mini player, that grows into a card the same way the mini player does (`MeetingCardView`, `MeetingCard.cs`; its rules in `Core/MeetingBar.cs`).** From up to an hour before: the meeting's name and a countdown ("8m") on the calendar's accent tint (`DriftCalendarEventBrush`). Hovered, or by itself from ten minutes before (and then it stays open), it opens into a card, WinUI's own (`CardBackgroundFillColorDefaultBrush` with a `CardStrokeColorDefaultBrush` hairline, as Settings' rows are), the row's tint fading out as it does (left under the card, the tint turned it peach): each line has its icon in the calendar icon's column (a clock beside "In 8 min" in the accent over "2:00 – 2:30 PM" in grey, never the two joined by a "·"; a camera beside the call's name; people beside their faces and "6 people"), with Join once it can be joined. ⌄ at its corner collapses it back to the row; × closes it; either way it's back, open, with Join five minutes before. The mini player growing pushes it up; neither covers the other. Join is the card's alone: the calendar's tile (or pinned row) shows only its countdown. The reminder a minute before (`MeetingNoticeView`) comes only when the card can't be seen: the sidebar hidden, the Library in its place, or the card closed. Arc had nothing like it (only the countdown on the calendar's favicon, which Drift's tile keeps); the user found that countdown too easy to miss.
  Why: a meeting about to start has to be seen without hovering anything; the row is always there, and the card comes to you when it matters.
  <!-- 2026-11-26: "i have a meeting in 46 minutes but its difficult to see" · chosen from three rounds of prototypes: "Opens itself", "it should be collapsible as well closable", "stacked. two buttons. the push is fine", "icon column" · on the reminder only when the card can't be seen, and the tile without Join: "yes." · "i don't like the design of the expanded card… use winui3's styling", then "card" over flyout and accent -->

- **Profiles are Dia's, laid out as Arc's Spaces: there are no Spaces, only Profiles, each its own sidebar and its own data. The Profile's name heads its tabs, under its own Essentials, and its icons sit between the Library button and + at the foot of the sidebar.** The one you're on is drawn full; a Profile with no icon of its own, and every Profile when there isn't room, is a dot (two globes side by side can't be told apart). Creating one takes the sidebar's place, as Arc's Create a Space does, without Arc's picture. Switching pages, as Arc's and Zen's sidebars do: every Profile has its own live sidebar (`ProfilePageView`), Essentials and all, side by side a 16px gap apart, cut off at the sidebar's edges, never drawn over the page. What slides in is that Profile's real sidebar, never a picture of it ("it should feel as natural as sliding a piece of paper across a table. why tf are we using a picture?"); its Essentials slide with it ("the essentials are supposed to move along with the sidebar as i swipe"). A swipe moves them with the fingers, 1:1, on the compositor, the next Profile coming in beside the one you're on at full strength (nothing fades); a click, Ctrl+1–9 or a mouse button slides the same way, at rest in about 160ms. Past the first or last Profile the sidebar stays put: no rubber band (the user tried one and didn't like it). The selected tab doesn't animate.
  <!-- 2026-11-26: "i think we should unify spaces and profiles. and just call it profiles. like how dia and safari do it" · "no more 'Space' just Profile" -->
  Why: the user asked for Arc's latest Spaces, copied; its layout and words were read from Arc for Windows 1.133, and its paging recorded frame by frame against it (the first slide drew over the page, faded, left the sidebar empty, then popped the next Space in nearly finished).
  <!-- 2026-11-24: "lets work on spaces ... see how arc did it (in the latest one) and copy it" · "go ahead with the paging rework. also, lets add rubber banding between the boundaries to its clear" · then "remove the rubber banding... i don't like your implementation" -->

- **Every popup and flyout has rounded corners (8px). A custom `FlyoutPresenter` style must set `CornerRadius`**, since its own style drops the default. Checked by `Drift.Tests/PopupStyleTests.cs`.
  Why: a popup with sharp corners looks broken next to the rest.
  <!-- 2026-11-17: "it also opens with sharp borders" (the Change icon popover) -->

## Text

- **Drift's interface is set in Segoe UI Variable Display, falling back to Segoe UI.**
  It's set once, in `Styles/Text.xaml` (`ContentControlThemeFontFamily`, and the TextBlock style built on it); nothing names a font of its own except emoji (Segoe UI Emoji) and icons.
  <!-- 2026-11-23: "Segoe UI Variable Display - font it should use" -->

- **Three sizes for words in the sidebar and its surfaces: 14 for what you pick by name (tabs, folders, menu rows), 13 for labels over a group and lines under a title, 12 for counts, badges and timecodes. Nothing you read is smaller than 12.**
  A label heading a group (the Space's name over its tabs) is 13 SemiBold in the secondary grey: the weight, not a smaller size, sets it apart from the tabs. Its area is Zen's: 44 tall with a 16 icon, and a tab's hover fill (36 tall, radius 8) inset within it (2026-11-25, at 28 tall with a 14 icon: "the space's title area is too small ... see how zen does it"). Still under 12, to bring up when next touched: the Library's tab captions and its history and bookmark sites, the mini player's timecodes, the calendar card's hours and Join (11), and an Essential's meeting countdown (10).
  Why: at 12 regular in a light grey the Space's name was hard to read; Arc's is 12 SemiBold and darker, and Drift goes a size up.
  <!-- 2026-11-24: "the text sizing for the space label... seems small and difficult to read. if there's no guidance in design.md, lets decide on something" -->

## Color

- **Cards in the sidebar are translucent white over the window's backdrop: `DriftFolderCardBrush` (#59FFFFFF light, #0EFFFFFF dark). Never in-app acrylic in the sidebar.**
  Why: the look wanted is "kinda transparent, kinda like acrylic"; in-app acrylic can't see the window's own backdrop (`DesktopAcrylicBackdrop`), so it renders solid white.
  <!-- 2026-11-17: "noo it should be kinda transparent (kinda like acrylic)" · "make it a bit more transparent" -->

- **Text reads at 4.5:1 or better against what's under it, on a hovered row too; icons, dots and other marks at 3:1. Checked by `Drift.Tests/ContrastTests.cs`.**
  Secondary text is `DriftSecondaryTextBrush` (#666666 light, #A0A0A0 dark), never a lighter grey or text faded with Opacity. A Profile you're not on is its icon at 60% (`ProfileOrder.IconStrength`).
  Why: the old grey (#888888) was 3.4:1 on the light sidebar, and the other Spaces' icons at 45% were 2.2:1.
  <!-- 2026-11-24: "check the spacing, color contrasts" -->

## Spacing & layout

- **When a row is short of room, its text truncates first. Its buttons are never pushed out or clipped.**
  Why: a button you can't see can't be used; the name can always be read on hover.
  <!-- 2026-11-17: "the x ... can get hidden when the sidebar is not wide enough (the folder name should truncate...)" -->

- **List rows (tabs, history, downloads…): titles and subtitles run to the row's edge.** Buttons shown only on hover must not keep a column or margin while hidden: collapse them (`Visibility.Collapsed`, not `Opacity` 0), and make the text give way only while they're showing, or swap with the icon as tab rows do. Check row text in a screenshot before reporting.
  Why: early truncation was flagged twice, on sidebar tabs and on history rows (an invisible Auto column of hover buttons cut the titles short).
  <!-- 2026-11-13: "remember not to do this again" -->

- **Keep Drift dense and quiet: list rows as tall as a sidebar tab (38px), 2–3px apart; shadows faint (the selected tab's is #05000000 in light mode).**
  Why: Arc's density; heavy shadows and spacing read as clumsy.
  <!-- 2026-11-16: "reduce the command bar items's heght. it seems really spaced out" · "reduce teh tab shadows and the main web area's shadow" -->

## Motion

- **Selection indicators switch instantly; they never slide between items.**
  Why: switching is frequent, and a slide reads as lag.
  <!-- 2026-11-16: "the active tab indicator for split tabs should not slide - just switch it immediately" -->

- **Popups open at once, even the first time.** Load their data and fonts at idle soon after start (as `WarmIconPickerSoon` does for the icon picker).
  Why: a slow first open feels broken.
  <!-- 2026-11-17: "the 'change icon' popover takes too long to load the first time (can we preload it?)" -->

- **The Library opens like a drawer, one thing after another: the tabs fade out (about 60ms), then the Library slides in from wholly off the window at full strength, never faded (about 250ms, easing out).** Shutting is quicker (Library gone in about 105ms, tabs back by about 220ms). A button or key never bounces; only fingers that let go moving do, a little (`LibrarySwipe.Motion`, tested by stepping the spring).
  Why: crossing its width in 90ms it seemed to appear rather than slide, and fading in across the tabs looked like two things on top of each other.
  <!-- 2026-11-24: "the library should slide in. not just appear"; "the sidebar should be almost faded out completely before the library comes sliding in. currently it looks like its overlapping and its ugly"; 2026-11-25: "refine the animation timing" -->

## Interaction

- **Every clickable row and button in the sidebar and popups has a hover state, and it's the inactive tab's hover (`DriftHoverBrush`), never white.**
  Why: hovers should look the same everywhere, as the tabs' do.
  <!-- 2026-11-16: "the new tab button becomes white when you hover. it should be like when you hover the inactive tabs" · "i don't see any hover state when i hover on this" -->

- **A hover fill has the same size, inset and radius as the selected card it could become; never larger.**
  Why: the hover previews the selection; a bigger fill looks like a different element.
  <!-- 2026-11-17: "hovering shouldn't look like that (on the right) since its not that big" -->

- **Small chrome affordances on the page (such as the page dots) rest small but clearly visible (about 80% opacity), grow when the pointer comes within about 56px, and act when it reaches them.** Never faint, never only on hover.
  Why: they must be easy to find, and coming near is enough of an ask.
  <!-- 2026-11-17: "should only enlarge when i bring my mouse close to it" · "when i'm even closer, it should open the menu for me" · "they should be more noticeable and prominent" -->

- **Resize grips (the split divider, the sidebar's edge) are invisible until the pointer is on their hit area, where the resize cursor shows.**
  Why: a grip at rest is clutter; the cursor tells you it's there.
  <!-- 2026-11-17: "it shouldn't show until the user places their mouse over the gap to drag (when the mouse switches to the up down arrow)" -->

- **Dragging to move shows the move cursor (`SizeAll`); dragging to resize, the resize cursor.**
  Why: the cursor confirms what the drag will do.
  <!-- 2026-11-17: "when dragging, the cursor should be that plus cursor with arrowheads at each point" -->

- **A new place that offers actions on something (a hover list, a pill, a command) follows the same rules as that thing's right-click menu.** Live folders never offer New tab.
  Why: the same thing shouldn't allow different actions depending on where you click.
  <!-- 2026-11-17: "isn't it weird that its supposedly affording me the ability to add a custom tab to a live folder??" -->

- **Never add buttons or menu items for features Drift doesn't have yet; put the feature in `Drift/ROADMAP.md`.**
  Why: a button that leads nowhere promises something Drift can't do.
  <!-- 2026-11-17: "you can hold off on extensions. we haven't built those yet" -->

## Copy

Drift's voice follows Arc's: see the `write-copy` skill (`.claude/skills/write-copy/SKILL.md`) for how each kind of string is worded. The rules below still apply on top.

- **Subtext only when it's needed to understand a control.** Default to the label alone; don't restate what the control already shows (no "Off for this site" under an unchecked toggle); keep status short ("Connection is secure").
  Why: redundant lines were removed from the site settings panel as noise.
  <!-- 2026-11-05: the toggle's "Blocking ads… / Off for this site" line and the encryption note removed -->

- **A line under a label is a whole thought, never a fragment leaning on the label: don't start it with a preposition or conjunction ("With", "From", "For", "And" and the like).** "With uBlock Origin Lite" becomes "Drift uses uBlock Origin Lite to block them."
  Why: a fragment reads as a broken sentence.
  <!-- 2026-11-20: "don't start sentences with 'With' like 'With uBlock blah blah'" -->

- **A row's subtext describes the setting; its value lives in the control on the right** (a folder in a picker, as Arc's Download location), never in the subtext.
  Why: every other row reads that way; a value in the subtext breaks the pattern.
  <!-- 2026-11-20: "'Save downloads to' has its value in the subtext instead of using the subtext as a description. that diverges from the rest of the ui" -->

- **Full stops: a single line of one sentence or less has none; two or more sentences end every sentence with one; an error written as a message, a confirmation body and an intro always end with one (a one-line status beside its button, such as "We couldn't reach Gmail", doesn't).** ("Your files stay where they are" · "Update in progress. Hang tight!" · "This action is permanent.")
  Why: Arc's pattern; the app had both kinds side by side under the same kind of label.
  <!-- 2026-11-20: "our use of punctuation in the app seems inconsistent. full stops especially" -->

- **Status lines speak only when something needs the person (signed out, couldn't reach it); never the same line repeated down a list.** All well, still loading, or not set up says nothing.
  Why: a status under every row is a wall of text.
  <!-- 2026-11-20: "so much text sigghhh" (the previews sheet with "Pin … to see it here" under four rows) -->

- **Write for an ordinary user, not an engineer: no lines describing internal behaviour.** If a line needs knowledge of how the browser works to make sense, drop it.
  Why: ordinary users wouldn't understand it.
  <!-- 2026-11-15: "If it recovers, it comes back by itself." cut from the page-hang card -->
