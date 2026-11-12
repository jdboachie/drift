using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Drift;

/// <summary>
/// One conversation with the Claude Code or Codex you've installed, run as a child process that
/// stays up between questions so follow-ups answer at once. Each is locked down to answering: no
/// files, no shell, none of your MCP servers, CLAUDE.md, AGENTS.md, skills or hooks; web search
/// only. It runs in an empty folder of Drift's (%LOCALAPPDATA%\Drift\ask) and neither CLI keeps the
/// conversation. Events arrive on a background thread.
/// </summary>
internal abstract class AiSession : IDisposable
{
    protected static readonly string AskFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drift", "ask");

    protected static string Instructions =>
        "You're answering a question typed into the command bar of Drift, a web browser. Answer directly and "
        + "concisely; use Markdown where it helps. Search the web when the question needs current information. "
        + $"Today is {DateTime.Now:dddd, d MMMM yyyy}.";

    private readonly string _exe;
    private Process? _process;
    private Task? _started;
    private Action<AiEvent>? _listener;
    private readonly object _gate = new();

    protected AiSession(AiProvider provider, string exe, string model)
    {
        Provider = provider;
        _exe = exe;
        Model = model;
    }

    public AiProvider Provider { get; }
    public string Model { get; }
    /// <summary>An answer's on its way.</summary>
    public bool Busy { get; private set; }
    public bool Alive => _process is { HasExited: false } && !_disposed;
    private bool _disposed;
    // Stopped by you: however it ends, that's the end, not a failure.
    private bool _stopping;

    /// <summary>Starts the process (once), so it's warm by the time there's a question.</summary>
    public Task StartAsync() => _started ??= Task.Run(StartCore);

    /// <summary>Asks; what comes back goes to onEvent, ending with AiDone or AiFailed.</summary>
    public async void Ask(string question, Action<AiEvent> onEvent)
    {
        Busy = true;
        _stopping = false;
        _listener = onEvent;
        try
        {
            await StartAsync();
            await SendQuestionAsync(question);
        }
        catch (Exception e)
        {
            Finish(new AiFailed(AiFailure.Other, e.Message));
        }
    }

    /// <summary>Stops the answer where it's got to. The conversation carries on.</summary>
    public void Stop()
    {
        if (!Busy) return;
        _stopping = true;
        try { SendStop(); }
        catch { Finish(new AiDone()); }
    }

    protected abstract IEnumerable<string> Arguments { get; }
    protected abstract Task HandshakeAsync();
    protected abstract Task SendQuestionAsync(string question);
    protected abstract void SendStop();
    protected abstract IReadOnlyList<AiEvent> Read(string line);

    /// <summary>Anything the process needs on disk before it starts.</summary>
    protected virtual void BeforeStart() { }

    private async Task StartCore()
    {
        Directory.CreateDirectory(AskFolder);
        BeforeStart();
        var info = new ProcessStartInfo
        {
            WorkingDirectory = AskFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (_exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            // npm's shims run through cmd; none of the arguments have spaces or quotes in them.
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            info.Arguments = $"/d /s /c \"\"{_exe}\" {string.Join(" ", Arguments)}\"";
        }
        else
        {
            info.FileName = _exe;
            foreach (var argument in Arguments) info.ArgumentList.Add(argument);
        }

        _process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start " + _exe);
        ChildProcesses.Adopt(_process);
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginErrorReadLine();
        new Thread(ReadLoop) { IsBackground = true, Name = $"{Provider} output" }.Start();
        await HandshakeAsync();
    }

    private void ReadLoop()
    {
        var output = _process!.StandardOutput;
        try
        {
            while (output.ReadLine() is { } line)
                foreach (var e in Read(line)) Handle(e);
        }
        catch { }
        if (Busy && !_disposed) Finish(new AiFailed(AiFailure.Other, $"{AskProviders.Name(Provider)} stopped unexpectedly."));
        Ended();
    }

    /// <summary>Process gone: whatever's waiting on a reply isn't getting one.</summary>
    protected virtual void Ended() { }

    protected virtual void Handle(AiEvent e)
    {
        if (e is AiDone or AiFailed) Finish(e);
        else if (Busy) _listener?.Invoke(e);
    }

    protected void Finish(AiEvent end)
    {
        Action<AiEvent>? listener;
        lock (_gate)
        {
            if (!Busy) return;
            Busy = false;
            listener = _listener;
            if (_stopping && end is AiFailed) end = new AiDone();
        }
        listener?.Invoke(end);
    }

    protected void WriteLine(string json)
    {
        var input = _process?.StandardInput ?? throw new InvalidOperationException("Not started");
        lock (_gate)
        {
            input.Write(json);
            input.Write('\n');
            input.Flush();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listener = null;
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
        catch { }
        _process?.Dispose();
    }
}

/// <summary>
/// Claude Code in print mode, taking one question after another as JSON on its input
/// ("--input-format stream-json"), which keeps the conversation going in the one process.
/// </summary>
internal sealed class ClaudeSession : AiSession
{
    private readonly ClaudeStream _stream = new();
    private int _requests;

    public ClaudeSession(string exe, string model) : base(AiProvider.Claude, exe, model) { }

    protected override IEnumerable<string> Arguments =>
    [
        "-p",
        "--input-format", "stream-json",
        "--output-format", "stream-json",
        "--verbose",
        "--include-partial-messages",
        // No CLAUDE.md, skills, plugins, hooks, MCP servers or memory; signed in as usual.
        "--safe-mode",
        "--tools", "WebSearch",
        "--allowedTools", "WebSearch",
        "--no-session-persistence",
        "--model", Model,
        "--system-prompt-file", "prompt.md",
    ];

    protected override Task HandshakeAsync() => Task.CompletedTask;

    // Read once, as it starts.
    protected override void BeforeStart() => File.WriteAllText(Path.Combine(AskFolder, "prompt.md"), Instructions);

    protected override Task SendQuestionAsync(string question)
    {
        WriteLine(JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { role = "user", content = question },
            parent_tool_use_id = (string?)null,
        }));
        return Task.CompletedTask;
    }

    protected override void SendStop() =>
        WriteLine(JsonSerializer.Serialize(new
        {
            type = "control_request",
            request_id = $"stop{Interlocked.Increment(ref _requests)}",
            request = new { subtype = "interrupt" },
        }));

    protected override IReadOnlyList<AiEvent> Read(string line) => _stream.Read(line);
}

/// <summary>
/// Codex's app server (JSON-RPC over its input and output): one thread for the conversation,
/// a turn per question.
/// </summary>
internal sealed class CodexSession : AiSession
{
    private readonly CodexStream _stream = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<CodexReply>> _waiting = new();
    private long _nextId;
    private string? _thread;
    private string? _turn;

    public CodexSession(string exe, string model) : base(AiProvider.Codex, exe, model) { }

    protected override IEnumerable<string> Arguments => ["app-server"];

    protected override async Task HandshakeAsync()
    {
        await RequestAsync("initialize", new JsonObject
        {
            ["clientInfo"] = new JsonObject { ["name"] = "drift", ["title"] = "Drift", ["version"] = "1.0" },
        });
        WriteLine("""{"method":"initialized"}""");

        var config = new JsonObject
        {
            ["features.shell_tool"] = false,
            ["web_search"] = "live",
            // No AGENTS.md from the folder it runs in (there's none, but nor from above it).
            ["project_doc_max_bytes"] = 0,
            ["history.persistence"] = "none",
        };
        // Your MCP servers stay off here: they'd only slow it down.
        var settings = await RequestAsync("config/read", new JsonObject());
        if (settings.Result is { } json && JsonNode.Parse(json)?["config"]?["mcp_servers"] is JsonObject servers)
            foreach (var (name, _) in servers) config[$"mcp_servers.{name}.enabled"] = false;

        var started = await RequestAsync("thread/start", new JsonObject
        {
            ["model"] = Model,
            ["cwd"] = AskFolder,
            ["approvalPolicy"] = "never",
            ["sandbox"] = "read-only",
            ["ephemeral"] = true,
            ["developerInstructions"] = Instructions,
            ["config"] = config,
        });
        _thread = started.Result is { } r ? JsonNode.Parse(r)?["thread"]?["id"]?.GetValue<string>() : null;
        if (_thread is null) throw new InvalidOperationException(started.Error ?? "Codex didn't start a conversation.");
    }

    protected override async Task SendQuestionAsync(string question)
    {
        var reply = await RequestAsync("turn/start", new JsonObject
        {
            ["threadId"] = _thread,
            ["input"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = question }),
        });
        if (reply.Error is { } error) Finish(AiErrors.Classify(error));
        else _turn = reply.Result is { } r ? JsonNode.Parse(r)?["turn"]?["id"]?.GetValue<string>() : null;
    }

    protected override void SendStop()
    {
        if (_thread is null || _turn is null) return;
        _ = RequestAsync("turn/interrupt", new JsonObject { ["threadId"] = _thread, ["turnId"] = _turn });
    }

    protected override IReadOnlyList<AiEvent> Read(string line) => _stream.Read(line);

    protected override void Handle(AiEvent e)
    {
        switch (e)
        {
            case CodexReply reply:
                if (_waiting.TryRemove(reply.Id, out var waiting)) waiting.TrySetResult(reply);
                break;
            case CodexAsked asked:
                // It's told never to ask; if it does anyway, the answer's no.
                WriteLine(JsonSerializer.Serialize(new { id = asked.Id, error = new { code = -32601, message = "Not supported" } }));
                break;
            default:
                base.Handle(e);
                break;
        }
    }

    protected override void Ended()
    {
        foreach (var (_, waiting) in _waiting) waiting.TrySetException(new InvalidOperationException("Codex stopped unexpectedly."));
    }

    private async Task<CodexReply> RequestAsync(string method, JsonObject parameters)
    {
        var id = Interlocked.Increment(ref _nextId);
        var waiting = new TaskCompletionSource<CodexReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[id] = waiting;
        WriteLine(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString());
        var done = await Task.WhenAny(waiting.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        if (done != waiting.Task)
        {
            _waiting.TryRemove(id, out _);
            throw new TimeoutException($"Codex didn't answer {method}.");
        }
        return await waiting.Task;
    }
}

/// <summary>Whether each CLI is installed and signed in, asked of the CLI itself.</summary>
internal static class AiReadinessCheck
{
    /// <summary>What's known, shared by the command bar and Settings; cleared when it may have changed.</summary>
    public static readonly ConcurrentDictionary<AiProvider, AiReadiness> Known = new();

    public static async Task<AiReadiness> CheckAsync(AiProvider provider)
    {
        if (AiLocator.Find(provider) is not { } exe) return AiReadiness.Missing;
        // "claude auth status" / "codex login status": 0 when signed in.
        string[] args = provider == AiProvider.Claude ? ["auth", "status"] : ["login", "status"];
        try
        {
            var info = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                info.Arguments = $"/d /s /c \"\"{exe}\" {string.Join(" ", args)}\"";
            }
            else
            {
                info.FileName = exe;
                foreach (var a in args) info.ArgumentList.Add(a);
            }
            using var process = Process.Start(info);
            if (process is null) return AiReadiness.Missing;
            ChildProcesses.Adopt(process);
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return AiReadiness.SignedOut;
            }
            return process.ExitCode == 0 ? AiReadiness.Ready : AiReadiness.SignedOut;
        }
        catch
        {
            return AiReadiness.Missing;
        }
    }

    /// <summary>Codex's models, as its app server lists them for your account; null if it can't.</summary>
    public static async Task<List<string>?> CodexModelsAsync()
    {
        if (AiLocator.Find(AiProvider.Codex) is not { } exe) return null;
        try
        {
            var info = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
            };
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                info.Arguments = $"/d /s /c \"\"{exe}\" app-server\"";
            }
            else
            {
                info.FileName = exe;
                info.ArgumentList.Add("app-server");
            }
            using var process = Process.Start(info);
            if (process is null) return null;
            ChildProcesses.Adopt(process);
            _ = process.StandardError.ReadToEndAsync();
            var input = process.StandardInput;
            await input.WriteAsync("""{"id":1,"method":"initialize","params":{"clientInfo":{"name":"drift","title":"Drift","version":"1.0"}}}""" + "\n");
            await input.WriteAsync("""{"method":"initialized"}""" + "\n" + """{"id":2,"method":"model/list","params":{}}""" + "\n");
            await input.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
                {
                    var node = JsonNode.Parse(line);
                    if (node?["id"]?.GetValue<long>() != 2) continue;
                    return node["result"]?["data"] is JsonArray data
                        ? data.Select(m => m?["id"]?.GetValue<string>() ?? m?["model"]?.GetValue<string>()).OfType<string>().ToList()
                        : null;
                }
            }
            finally
            {
                try { process.Kill(true); } catch { }
            }
        }
        catch { }
        return null;
    }
}

/// <summary>
/// Every process Drift starts for AI goes in one Windows job that closes with Drift, so none
/// outlives it, even if Drift crashes.
/// </summary>
internal static class ChildProcesses
{
    private static readonly Lazy<IntPtr> Job = new(CreateJob);

    public static void Adopt(Process process)
    {
        try
        {
            if (Job.Value != IntPtr.Zero) AssignProcessToJobObject(Job.Value, process.Handle);
        }
        catch { }
    }

    private static IntPtr CreateJob()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return job;
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            SetInformationJobObject(job, 9 /* JobObjectExtendedLimitInformation */, pointer, (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
}
