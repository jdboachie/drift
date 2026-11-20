# Drift design rules

Rules learned from building Drift, each with why. Read before building or changing UI.

## Components

- **Build from the shared pieces; never copy one.** Before writing a panel, row, card or style, look for it here:
  - `Views/`: the window's regions (caption bar, toolbar, address bar, command bar, switcher, Library, downloads peek, mini player, meeting notice, crash card, drag ghosts). A new region is a view of its own, with an `Owner` the window sets; its handlers stay in the window's code.
  - `Controls/`: what's used in more than one place. A tab's row (`TabRowView`), an Essential's tile, a folder's header, a page's card (`PageCardView`, also Peek's), handle and divider, settings rows and groups (`SettingsRow` in a `SettingsGroup`), `Toast`, `HoverCard`. A control in a list's row finds its window with `MainWindow.Of(this)`.
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

- **Anything grouping tabs in the sidebar (folders, splits) uses one card: `DriftFolderCardBrush`, a `DriftFolderCardBorderBrush` hairline, corner radius 10.**
  The card shows only while the group is open (a folder) or in front (a split); closed or in the background, it's a plain row.
  Why: one card style makes groups read alike; a card on a closed group is noise.
  <!-- 2026-11-17: "the split view should have the same card treatment as the folders - just the color and border" · "the split row when it isn't open shouldn't have that folder-card treatment" -->

- **In a group, the item in front is marked with the normal selected-tab card (`SplitFrontCardStyle`: `DriftSelectedBrush`, `DriftSelectedEdgeBrush`, radius 8) on the group's card. Never a faint tint.**
  Why: you must be able to tell at a glance which one is active.
  <!-- 2026-11-17: "i can't tell which split is active by looking at the tab. look at how arc (or zen) does it" -->

- **Every popup and flyout has rounded corners (8px). A custom `FlyoutPresenter` style must set `CornerRadius`**, since its own style drops the default. Checked by `Drift.Tests/PopupStyleTests.cs`.
  Why: a popup with sharp corners looks broken next to the rest.
  <!-- 2026-11-17: "it also opens with sharp borders" (the Change icon popover) -->

## Color

- **Cards in the sidebar are translucent white over the window's backdrop: `DriftFolderCardBrush` (#59FFFFFF light, #0EFFFFFF dark). Never in-app acrylic in the sidebar.**
  Why: the look wanted is "kinda transparent, kinda like acrylic"; in-app acrylic can't see the window's own backdrop (`DesktopAcrylicBackdrop`), so it renders solid white.
  <!-- 2026-11-17: "noo it should be kinda transparent (kinda like acrylic)" · "make it a bit more transparent" -->

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
