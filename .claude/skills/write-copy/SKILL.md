---
name: write-copy
description: Write or review the words in Drift's interface — setting labels, section intros, notes, errors, confirmations, toasts, empty states, tooltips, menu items, onboarding — in the voice of Arc (The Browser Company), which Drift's copy follows. Use whenever adding or changing any user-facing string, naming a setting, or wording an error, prompt or empty state; and when asked to review or rewrite copy.
---

# Writing Drift's copy

Drift talks to people the way Arc does: plainly, warmly, in whole sentences, owning its problems, and only playful where nothing is at stake. Distilled from about 600 of Arc's own strings; **[REFERENCE.md](REFERENCE.md) has Arc's wording for every surface** — read the section for what you're writing.

DESIGN.md › Copy applies on top: subtext only when it's needed; write for an ordinary user, nothing about internals.

## The voice in one breath

Say what will happen, in a sentence, to "you". Explain a feature once, with an everyday example. Put limits in a quiet note. When something goes wrong, say so plainly, take it on yourselves, and say what to do. Confirm with short receipts. Be warm where there's room — never when someone could lose something.

## Rules by surface

**Setting labels** — the behaviour, as a sentence, with its condition ("Open a Peek window when clicking on links with Shift held"), never a noun stub ("Shift-click behaviour"). Reads true when on; no "Disable…" toggles. With a dropdown, **every option finishes the label's sentence on its own** — no label ending in a preposition the options must complete ("…after" + "Never"). And pick the verb that needs no disclaimer: "Keep downloads in the list" + "For a week / Forever", not "Remove… after" + "Your files stay where they are".

**Section intros** — explain a feature once, above its group: what it is, then when it's useful, with an everyday example ("X is a … It's perfect for …"). Two sentences at most; only for features that need it.

**Lines under a label** — a whole thought, never a fragment leaning on the label: don't open with a preposition or conjunction ("With…", "From…", "For…", "And…", "Via…", "In…"). A standalone phrase is fine ("Your files stay where they are"). Subtext describes; a setting's **value lives in the control on the right**, never in the subtext. Limits go here as a quiet note ("Note: This only affects…").

**Errors** — what happened in plain words, owned ("We couldn't…"), then what to do. No codes or exception text. "Oops" only for small, recoverable failures; security and data errors stay flat.

**Confirmations** — a question naming the thing; one sentence of consequence; the way back, or "This action is permanent." The button says the action ("Delete Space"), not "OK".

**Toasts** — a short past-tense receipt with no full stop ("Bookmark removed"); at most one verb action ("Edit this setting"); work in progress ends in "…".

**Empty states** — a friendly title, and how to fill it ("Nothing here yet!" / "Hover over a tab and press the X to archive it.").

**Tooltips** — for icon-only buttons, hidden gestures ("Click to go back, hold to see history"), a state ("The built-in ad blocker is off. You can change this in Settings."), or text that may be cut off. A button whose label says it all gets none.

**Menu items** — specific verb phrases naming the object ("Duplicate current tab"); "…" when it asks for more before acting.

**Onboarding and prompts** — where warmth lives; even a decline button can have personality. Exclamation marks here and in empty states only.

**Trade-offs** — say the cost in the same breath as the benefit ("…can really slow down your browser!").

## Craft

- Contractions everywhere: we're, can't, you'll.
- Curly apostrophes (’), a real ellipsis (…), em dashes (—), curly quotes around names (“Work”) — typed as characters in XAML and C#.
- Features are proper nouns, named one way everywhere: Peek, Essentials, Split View, Live Folders.
- **Full stops**, as Arc uses them:
  1. A single line of at most one sentence has none — labels, buttons, menu items, options, subtext, notes, toasts, tooltips, empty-state titles ("Your files stay where they are", "Bookmark removed").
  2. Two or more sentences: every sentence ends with one, the last included ("Update in progress. Hang tight!").
  3. An error written as a message (what happened, plus what to do), a confirmation body and an intro always end with one — they're read as sentences ("This action is permanent."). A one-line status on a card, beside its button, follows rule 1 ("We couldn't reach Gmail" · Try again).
  4. "?" and "!" stay wherever they belong.
- Key hints say what to do, as a sentence: "Press Esc to cancel", not "Esc cancels".
- Numbers as digits ("3 tabs"). "Learn more" only when there's somewhere real to send them.

## Before you ship a string

1. Does a setting's label say what happens, as a sentence, with its condition?
2. Is a feature explained once, in an intro? Is every line under a label a whole thought, and a description rather than a value?
3. Does an error say what happened, own it, and give the next step — with no internals?
4. Does a confirmation name the thing, give the consequence, and say whether there's a way back?
5. Is a toast a short receipt with no full stop?
6. Is a tooltip adding something the label doesn't?
7. Is the playfulness only where nothing is at stake?
8. Would an ordinary user understand every word? Curly apostrophes, real ellipses, contractions?

## Provisional decisions

The user approved this reading of Arc's voice and will refine it. Until then:
- Menus and buttons: sentence case (Windows), not Arc's Title Case.
- "We" for apologies and promises; "Drift" as the subject when describing what the app does.
- Setting labels: "you" by default; "I/my" allowed for the person's own choices.
- Playfulness: empty states, first-run and prompts only.
