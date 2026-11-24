using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Drift;

/// <summary>
/// Draw, Erase, Highlight and Add text in PDFs, as in Edge. WebView2 ships Edge's PDF viewer with
/// those tools switched off, on purpose and with no setting to turn them back on, so Drift turns
/// them on inside the viewer as it starts. docs/pdf-annotations.md explains how, and what to check
/// when a new WebView2 runtime breaks it.
/// <para>
/// The viewer is a page of its own (a "guest" in the tab's page), which a tab's DevTools session
/// can't reach by itself. A hidden view per window watches for viewers starting, attaches to each
/// one from its own DevTools session while it's still blank, and gives it a script to run before
/// its own: in the viewer, one that turns the tools on where its settings arrive; in the viewer's
/// page, one that gives the answer the engine never gives (where to open a text box you clicked for).
/// </para>
/// </summary>
internal sealed class PdfTools(IntPtr window, bool isPrivate)
{
    /// <summary>Viewers some window is already looking after (two regular windows see the same ones).</summary>
    private static readonly HashSet<string> Claimed = new();

    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private string? _profile;
    private bool _starting;
    // Viewer target → its session, so a viewer is attached once.
    private readonly Dictionary<string, string> _viewers = new();

    /// <summary>Starts watching, once; a second call does nothing.</summary>
    public async void Start()
    {
        if (_starting) return;
        _starting = true;
        try
        {
            var environment = await MainWindow.EnvironmentAsync();
            var parent = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)window);
            _controller = isPrivate
                ? await environment.CreateCoreWebView2ControllerAsync(parent, MainWindow.PrivateOptions(environment))
                : await environment.CreateCoreWebView2ControllerAsync(parent);
            _controller.IsVisible = false;
            var core = _core = _controller.CoreWebView2;
            Listen(core, "Target.targetCreated", OnTargetSeen);
            Listen(core, "Target.targetInfoChanged", OnTargetSeen);
            Listen(core, "Target.targetDestroyed", OnTargetGone);
            Listen(core, "Target.attachedToTarget", OnAttached);
            // Its own profile, so a private window's viewers are its own and the regular ones aren't.
            using (var me = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Target.getTargetInfo", "{}")))
                _profile = me.RootElement.GetProperty("targetInfo").TryGetProperty("browserContextId", out var id) ? id.GetString() : null;
            await core.CallDevToolsProtocolMethodAsync("Target.setDiscoverTargets", """{"discover":true}""");
            Log("watching for PDF viewers");
        }
        catch (Exception e)
        {
            // No PDF tools then; PDFs still open and read as before.
            Log($"PDF tools didn't start: {e.Message}");
            Close();
        }
    }

    public void Close()
    {
        lock (Claimed) foreach (var viewer in _viewers.Keys) Claimed.Remove(viewer);
        _viewers.Clear();
        _receivers.Clear();
        try { _controller?.Close(); } catch { }
        _controller = null;
        _core = null;
    }

    /// <summary>
    /// How it went, in pdf.log beside the session: what was attached and patched (never which PDF), to
    /// tell why the tools are missing after a WebView2 update. Kept to its last couple of hundred lines.
    /// </summary>
    private static void Log(string line)
    {
        try
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift");
            var path = System.IO.Path.Combine(folder, "pdf.log");
            System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            if (new System.IO.FileInfo(path).Length > 64 * 1024)
                System.IO.File.WriteAllLines(path, System.Linq.Enumerable.TakeLast(System.IO.File.ReadAllLines(path), 200));
        }
        catch
        {
            // Nowhere to write it.
        }
    }

    // Held on to: once a receiver is collected, its events stop arriving (they did, after the first viewer).
    private readonly List<CoreWebView2DevToolsProtocolEventReceiver> _receivers = new();

    private void Listen(CoreWebView2 core, string name, Action<string, JsonElement> handle)
    {
        var receiver = core.GetDevToolsProtocolEventReceiver(name);
        _receivers.Add(receiver);
        receiver.DevToolsProtocolEventReceived += (_, e) =>
        {
            try
            {
                // A copy: the handlers carry on after an await, past the document's disposal.
                using var message = JsonDocument.Parse(e.ParameterObjectAsJson);
                handle(e.SessionId ?? "", message.RootElement.Clone());
            }
            catch (Exception ex)
            {
                Log($"{name}: {ex.Message}");
            }
        };
    }

    /// <summary>
    /// A viewer appearing. It starts blank (no address), then gets the viewer's: attached while blank,
    /// so the script below is in place before the viewer's own first script runs.
    /// </summary>
    private async void OnTargetSeen(string session, JsonElement message)
    {
        if (_core is not { } core || session.Length > 0) return;
        var info = message.GetProperty("targetInfo");
        var target = info.GetProperty("targetId").GetString()!;
        var kind = PdfViewers.Classify(info.GetProperty("type").GetString(), info.GetProperty("url").GetString());
        if (kind == PdfTarget.None)
        {
            // A blank one that turned out to be something else: let it go.
            if (_viewers.Remove(target, out var other))
            {
                lock (Claimed) Claimed.Remove(target);
                if (other.Length > 0)
                    try { await core.CallDevToolsProtocolMethodAsync("Target.detachFromTarget", JsonSerializer.Serialize(new { sessionId = other })); } catch { }
            }
            return;
        }
        if (_profile is not null && info.TryGetProperty("browserContextId", out var profile) && profile.GetString() != _profile) return;
        lock (Claimed)
        {
            if (_viewers.ContainsKey(target) || !Claimed.Add(target)) return;
            _viewers[target] = "";
        }
        try
        {
            using var attached = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Target.attachToTarget",
                JsonSerializer.Serialize(new { targetId = target, flatten = true })));
            var sessionId = attached.RootElement.GetProperty("sessionId").GetString()!;
            _viewers[target] = sessionId;
            // Sent together, not one after another: every round trip is time the viewer is loading in.
            // Two ways in: the script for its next document, if it hasn't made it yet, and the same
            // script run in the one it has, if it has.
            var now = core.CallDevToolsProtocolMethodForSessionAsync(sessionId, "Runtime.evaluate",
                JsonSerializer.Serialize(new { expression = ToolsOn, returnByValue = true })).AsTask();
            await Task.WhenAll(
                core.CallDevToolsProtocolMethodForSessionAsync(sessionId, "Page.addScriptToEvaluateOnNewDocument",
                    JsonSerializer.Serialize(new { source = ToolsOn })).AsTask(),
                now,
                core.CallDevToolsProtocolMethodForSessionAsync(sessionId, "Page.enable", "{}").AsTask(),
                // The page itself is a frame of the viewer's own: attached paused at its start, for the script it needs.
                core.CallDevToolsProtocolMethodForSessionAsync(sessionId, "Target.setAutoAttach",
                    """{"autoAttach":true,"waitForDebuggerOnStart":true,"flatten":true}""").AsTask());
            using var answer = JsonDocument.Parse(await now);
            var state = answer.RootElement.GetProperty("result").TryGetProperty("value", out var v) ? v.GetString() : null;
            Log($"attached to a {(kind == PdfTarget.Viewer ? "viewer" : "blank page that may become one")}: {state ?? "no answer"}");
            if (state == "late") StartAgain(core, sessionId);
        }
        catch (Exception e)
        {
            Log($"couldn't attach to a viewer: {e.Message}");
        }
    }

    private void OnTargetGone(string session, JsonElement message)
    {
        if (session.Length > 0) return;
        var target = message.GetProperty("targetId").GetString() ?? "";
        if (!_viewers.Remove(target)) return;
        lock (Claimed) Claimed.Remove(target);
    }

    /// <summary>A frame of a viewer's, paused at its start: the viewer's page gets its script, then everything carries on.</summary>
    private async void OnAttached(string session, JsonElement message)
    {
        if (_core is not { } core || session.Length == 0) return;
        var child = message.GetProperty("sessionId").GetString()!;
        try
        {
            if (message.GetProperty("targetInfo").GetProperty("type").GetString() == "iframe")
            {
                await core.CallDevToolsProtocolMethodForSessionAsync(child, "Page.enable", "{}");
                await core.CallDevToolsProtocolMethodForSessionAsync(child, "Page.addScriptToEvaluateOnNewDocument",
                    JsonSerializer.Serialize(new { source = TextBoxAnswer }));
                Log("ready for a viewer's page");
            }
        }
        catch (Exception e)
        {
            Log($"couldn't prepare a viewer's page: {e.Message}");
        }
        finally
        {
            try { await core.CallDevToolsProtocolMethodForSessionAsync(child, "Runtime.runIfWaitingForDebugger", "{}"); } catch { }
        }
        if (message.GetProperty("targetInfo").GetProperty("type").GetString() == "iframe") StartAgainIfLate(core, session);
    }

    private static DateTime _lastStartedAgain;

    /// <summary>
    /// The viewer makes its page once it has read its settings: if they came without the tools, it
    /// started before Drift got to it (when Drift starts, everything loads at once). The viewer
    /// alone starts again, which Drift catches in time; nothing's been drawn on it yet. Once at a
    /// time, so a viewer that's always too quick can't start over and over.
    /// </summary>
    private static async void StartAgainIfLate(CoreWebView2 core, string viewer)
    {
        try
        {
            using var asked = JsonDocument.Parse(await core.CallDevToolsProtocolMethodForSessionAsync(viewer, "Runtime.evaluate",
                """{"expression":"window.loadTimeData && loadTimeData.data_ ? loadTimeData.data_.pdfAnnotationsEnabled : null","returnByValue":true}"""));
            if (!asked.RootElement.GetProperty("result").TryGetProperty("value", out var on) || on.ValueKind != JsonValueKind.False) return;
            StartAgain(core, viewer);
        }
        catch (Exception e)
        {
            Log($"couldn't check a viewer: {e.Message}");
        }
    }

    private static async void StartAgain(CoreWebView2 core, string viewer)
    {
        if (DateTime.UtcNow - _lastStartedAgain < TimeSpan.FromSeconds(3))
        {
            Log("a viewer started without the tools; already started one again just now, so not this one");
            return;
        }
        _lastStartedAgain = DateTime.UtcNow;
        Log("a viewer started without the tools: starting it again");
        try { await core.CallDevToolsProtocolMethodForSessionAsync(viewer, "Page.reload", "{}"); }
        catch (Exception e) { Log($"couldn't start a viewer again: {e.Message}"); }
    }

    /// <summary>
    /// Runs in the viewer, as early as it can: its settings come from chrome.resourcesPrivate.getStrings,
    /// where WebView2 has every annotation switch off, and are stored as window.loadTimeData. Turned on
    /// before anything reads them: where they're asked for, if that's still to come, and where they're
    /// stored, if they've been asked for but haven't come back. Says "late" if they're already stored.
    /// </summary>
    private const string ToolsOn = """
        (() => {
          if (!location.href.includes('/edge_pdf/')) return 'elsewhere';
          const on = ['pdfAnnotationsEnabled', 'pdfInkingEnabled', 'pdfInkTransparencyEnabled', 'pdfHighlightModeEnabled',
            'pdfFreeTextEnabled', 'pdfTextNoteEnabled', 'pdfAnnotationsVisibilityEnabled'];
          const turnOn = (settings) => { if (settings) for (const k of on) settings[k] = true; };
          if (window.loadTimeData && window.loadTimeData.data_) return window.loadTimeData.data_.pdfAnnotationsEnabled ? 'on' : 'late';
          const rp = window.chrome && chrome.resourcesPrivate;
          if (rp && rp.getStrings && !rp.getStrings.__drift) {
            const real = rp.getStrings;
            const wrapped = function (component, callback) {
              return real.call(this, component, (strings) => { turnOn(strings); callback(strings); });
            };
            wrapped.__drift = true;
            rp.getStrings = wrapped;
          }
          const stored = Object.getOwnPropertyDescriptor(window, 'loadTimeData');
          if (!stored || (stored.configurable && !stored.get)) {
            let value = window.loadTimeData;
            Object.defineProperty(window, 'loadTimeData', {
              configurable: true, enumerable: true,
              get() { return value; },
              set(v) { try { turnOn(v && v.data_); } catch (e) { } value = v; },
            });
          }
          return 'on';
        })()
        """;

    /// <summary>
    /// Runs first in the viewer's page, and waits for it to start: in Add text, a click goes to the
    /// engine, which should answer "open a text box here". WebView2's never does, though it saves
    /// the text once a box exists. So if it hasn't answered in a moment, the box opens where the
    /// click landed.
    /// </summary>
    private const string TextBoxAnswer = """
        (() => {
          const install = () => {
            const m = window.viewerInternal && viewerInternal.pdfAnnotationManager_;
            if (!m) return false;
            if (m.__drift) return true;
            m.__drift = true;
            let answered = 0;
            const fromEngine = m.handleFreeTextEvent;
            m.handleFreeTextEvent = function (e) {
              if (e && /^show/.test(e.type)) answered = Date.now();
              return fromEngine.apply(this, arguments);
            };
            const toEngine = m.handleFreeTextUIEvent;
            m.handleFreeTextUIEvent = function (name, data) {
              const result = toEngine.apply(this, arguments);
              if (name === 'free-text-pointer-event' && data && data.eventType === 'click' && data.button === 0) {
                const asked = Date.now();
                setTimeout(() => {
                  if (answered >= asked) return;
                  const doc = document.querySelector('#internal-document-container > div[role=document]');
                  if (!doc) return;
                  const box = doc.getBoundingClientRect();
                  const viewport = window.pdfAdaptor && pdfAdaptor.getViewport && pdfAdaptor.getViewport();
                  const top = (viewport && viewport.embedTopOffset) || 0;
                  try { this.handleFreeTextEvent({ type: 'showAddFreeTextBox', x: data.event.x - box.left, y: data.event.y + top - box.top }); }
                  catch (err) { console.warn('Drift: add text', err); }
                }, 60);
              }
              return result;
            };
            return true;
          };
          let tries = 0;
          const wait = setInterval(() => { if (install() || ++tries > 300) clearInterval(wait); }, 100);
        })();

        """;
}
