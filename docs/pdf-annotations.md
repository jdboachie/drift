# Drawing, highlighting and adding text in PDFs

Drift opens PDFs in Edge's own PDF viewer, which comes with WebView2. In Edge, that viewer has
**Draw**, **Erase**, **Highlight** and **Add text**. In WebView2, Microsoft switches all four
off, on purpose: annotations were turned off for WebView2 in May 2021
(MicrosoftEdge/WebView2Announcements#21), and there are "no plans" to bring them back
(MicrosoftEdge/WebView2Feedback#1638, #2050). No setting, policy or browser switch turns them on.

Drift turns them on anyway, from the inside. This page says how, so it can be fixed when a new
WebView2 runtime breaks it, and what was tried and didn't work, so nobody tries it again.

Code: `Drift/PdfTools.cs` (the work), `Drift/Core/PdfViewers.cs` (what's what, tested in
`Drift.Tests/PdfViewersTests.cs`), and a few lines in `Drift/Confirm.cs` (asking before you lose
edits). Found on runtime 158.0.4370.33.

## What you get

- Draw, Erase and Highlight (on text, or free-form), with the viewer's own colours and sizes.
- Add text: click where it should go, type, click away.
- Save (the disk button, or Ctrl+S) writes the changes into the PDF file itself. Checked: a
  stroke drawn in Drift is an `/Ink` annotation in the saved file.
- Unsaved edits put a `*` before the tab's title, as in Edge. Closing or unloading the tab then
  asks first ("Leave test.pdf?"), and going to another page from it shows the engine's own
  "Leave site?".

## How the viewer is built (what matters here)

- The viewer is a page of Edge's built-in PDF extension,
  `chrome-extension://mhjfbmdgcfjbbpaeojofohoefgiehjai/edge_pdf/index.html`. It isn't part of the
  tab's page: it's a *guest* page inside it. In DevTools terms it's a target of type `webview`,
  which starts as a target of type `other` with no address and then navigates to the viewer.
- Its settings come from `chrome.resourcesPrivate.getStrings(PDF, callback)`. The callback stores
  them as `window.loadTimeData` (`e.data = strings; window.loadTimeData = e`). In WebView2 they
  include `is_webview2: true` and these, all false: `pdfAnnotationsEnabled`, `pdfInkingEnabled`,
  `pdfInkTransparencyEnabled`, `pdfHighlightModeEnabled`, `pdfFreeTextEnabled`,
  `pdfTextNoteEnabled`, `pdfAnnotationsVisibilityEnabled`.
- The viewer reads them **once, as it starts**. Its annotation manager is only created then, and
  only if annotations are on. Turning them on later shows the buttons, but nothing draws.
- The pages themselves are drawn in a frame of the viewer's own (an `iframe` target, at the PDF's
  address), running `pdf_internal_plugin.bundle.js` and `pdf_content_view.bundle.js`. That frame's
  `window.viewerInternal.pdfAnnotationManager_` talks to the PDF engine (the
  `application/x-google-chrome-pdf` plugin).

## What Drift does

### 1. Watching for viewers

A tab's DevTools session (`CoreWebView2.CallDevToolsProtocolMethodAsync`) can't reach the viewer:
it's a guest, and `Target.setAutoAttach` on the tab's page doesn't include guests. But a page
session may use `Target.setDiscoverTargets` and `Target.attachToTarget`, and discovery sees every
target in the browser.

So each window keeps a **hidden view** (`PdfTools`, started with the window's first page, like the
ad blocker's `AdBlockHelper`). It turns discovery on and attaches to every viewer, and to every
blank `other` target, since that's how a viewer starts. It only takes targets in its own profile
(`browserContextId`), so a private window's viewers are its private window's. A static set stops
two regular windows both attaching to the same viewer.

Keep the `CoreWebView2DevToolsProtocolEventReceiver` objects alive (`_receivers`). If one is
garbage-collected, its events stop arriving, silently. Here, they stopped after the first viewer.

### 2. Turning the tools on, before the viewer reads its settings

Right after attaching, Drift sends these together, since every round trip is time the viewer
spends loading:

- `Page.addScriptToEvaluateOnNewDocument` with the switch-on script (`ToolsOn`). It runs first in
  the viewer's document, if that document doesn't exist yet.
- `Runtime.evaluate` of the same script. It covers a document that already exists.
- `Page.enable`. The new-document script doesn't run without it.
- `Target.setAutoAttach` with `waitForDebuggerOnStart`, for the viewer's page frame (step 3).

The script wraps `chrome.resourcesPrivate.getStrings`, so the settings come back with the seven
switches on. It also catches the `window.loadTimeData` assignment, in case the settings were
already asked for but haven't come back yet. It answers `on`, `elsewhere` (not the viewer: a
blank document) or `late` (the settings had already arrived with the tools off).

When it's `late`, or the viewer's page appears and the settings say off, Drift reloads **just the
viewer** (`Page.reload` on its session). That makes a new viewer target, which Drift then catches
in time. Nothing can have been drawn yet. It's limited to once every 3 seconds, so a viewer that's
always too quick can't loop. In practice this happens, if at all, only for PDFs restored when Drift
starts, when everything loads at once.

### 3. Add text: the missing answer

In Add text, a click on the page goes to the engine as a `pointerEvent` (from
`handleFreeTextUIEvent('free-text-pointer-event', …)`). Edge's engine answers
`showAddFreeTextBox`, and the viewer opens a text box there. **WebView2's engine never answers.**
It does save the text, though, once a box exists: `createFreeTextAnnotation` comes back
`freeTextAdded: true` and the PDF is marked edited.

So the page frame gets a second script (`TextBoxAnswer`). It's added while the frame is paused at
its start, and it waits for `viewerInternal.pdfAnnotationManager_` to exist. If a left click in Add
text gets no `show…` answer within 60 ms, the script answers for the engine:
`handleFreeTextEvent({ type: 'showAddFreeTextBox', x, y })`. The `x` and `y` are the click's, made
relative to the document area, `#internal-document-container > div[role=document]`.

Without this, Add text only puts a focus outline around the whole page, which is what you saw at
first.

### 4. Not losing edits

The viewer's edits aren't the page's: the page's `beforeunload` knows nothing about them, so
Drift's own check (Confirm.cs) would have let a tab with edits close without asking. For a page
whose `document.contentType` is `application/pdf`, the check now asks the tab's title instead.
The viewer puts a `*` before it while there are unsaved edits.

## When it breaks

`%LOCALAPPDATA%\Drift\pdf.log` says what happened. It never says which PDF. A healthy viewer
looks like this:

```
watching for PDF viewers
attached to a blank page that may become one: on        (or: elsewhere)
ready for a viewer's page
```

- **No "watching" line.** The hidden view didn't start; the line before says why.
- **"attached" lines but the tools are off.** Edge's viewer has changed:
  - Do the settings still come from `chrome.resourcesPrivate.getStrings`, and are they still
    stored as `loadTimeData`?
  - Do the seven keys have the same names?
  
  Open a PDF in the demo, attach to the `webview` target over its debugging port, and look at
  `loadTimeData.data_`.
- **"a viewer started without the tools" on every PDF.** Drift is attaching too late, even after
  the restart. Look at what got slower.
- **Draw works but Add text doesn't open a box.** In the page frame, check that these still exist
  with the same names:
  - `viewerInternal.pdfAnnotationManager_.handleFreeTextUIEvent` / `handleFreeTextEvent`
  - the `showAddFreeTextBox` message
  - `#internal-document-container > div[role=document]`
  
  If a future runtime answers clicks itself, the 60 ms fallback stays out of the way.

## What didn't work (don't try these again)

- **Browser switches.** Edge's feature names are in `msedge.dll`: `msPdfInking`,
  `msPdfHighlightMode`, `msPdfFreeText`, `msPdfFreeTextOnNewViewer`, `msPdfTextNote`,
  `msPdfUndoRedo`… Passing them with `--enable-features` in `AdditionalBrowserArguments` changes
  nothing. WebView2 decides regardless. The `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` variable is
  ignored too, since Drift sets `AdditionalBrowserArguments`.
- **Turning the settings on after the viewer has started.** Both `loadTimeData.overrideValues` and
  replacing the React UI's Redux `featureFlags` and dispatching show the buttons, but nothing
  draws (see above).
- **Rewriting the viewer's scripts as they load** (`Fetch.enable` + `Fetch.fulfillRequest`). This
  works from a *browser-wide* DevTools session. That was the first proof, over the demo's
  debugging port. It doesn't work from a page or viewer session: Fetch on those never sees the
  extension's scripts, though Network does. A browser-wide session would mean a remote debugging
  port, which would open your browser to any program on the computer. Not for real use.
- **Pausing the viewer before its first script** (`Debugger.setInstrumentationBreakpoint`
  `beforeScriptExecution`). The viewer froze for good: it didn't even answer a second debugger.
  You get a blank PDF, which is worse than no tools.
- **`Page.addScriptToEvaluateOnNewDocument` without `Page.enable`.** It's accepted, but never runs.

## Not tried yet

- Editing a text box that's already there, by clicking it. The engine may not answer that either.
- Comments (text notes). The switch is on, but they're untested.
- Pen and touch input, and private windows.
- PDFs from the web (all testing was on a local file). The viewer is the same, but its restart
  fetches the PDF again.

## Testing it

The demo (`.claude/skills/demo`) has a debugging port, so a script can drive the viewer the way a
mouse would without touching the real mouse:

- Press a toolbar button inside the `webview` target.
- Send `Input.dispatchMouseEvent` drags for Draw, Erase and Highlight.
- For Add text, send clicks and `Input.insertText` to the PDF's `iframe` target.

Check `loadTimeData.data_.pdfAnnotationsEnabled` in the viewer, and
`viewerInternal.pdfAnnotationManager_.__drift` in the page frame.

Look at *every* `webview` and `iframe` target. A reopened PDF can leave an old one listed, and
"the first one that matches" may be the wrong one.
