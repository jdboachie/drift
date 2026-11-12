using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Drift;

// Ask AI (AskAiBar.cs): what's typed in the command bar, sent with Tab to the Claude Code or
// Codex you already have, signed in as you. This is the part with no window or process in it.

public enum AiProvider { Claude, Codex }

public enum AiReadiness { Ready, SignedOut, Missing }

public enum AiActivity { Thinking, Searching }

public enum AiFailure { Limited, SignedOut, Unreachable, Other }

/// <summary>What comes back while an answer is on its way.</summary>
public abstract record AiEvent;
/// <summary>More of the answer.</summary>
public sealed record AiText(string Text) : AiEvent;
/// <summary>What it's busy with before it says anything (or between).</summary>
public sealed record AiStatus(AiActivity Activity) : AiEvent;
/// <summary>The answer's finished, or was stopped.</summary>
public sealed record AiDone : AiEvent;
/// <param name="ResetsAt">For a usage limit, when it lifts, if it's said.</param>
public sealed record AiFailed(AiFailure Failure, string Message, DateTimeOffset? ResetsAt = null) : AiEvent;
/// <summary>Codex answering one of Drift's requests: its result as JSON, or its error.</summary>
public sealed record CodexReply(long Id, string? Result, string? Error) : AiEvent;
/// <summary>Codex asking Drift something (an approval, say), which Drift always turns down.</summary>
public sealed record CodexAsked(long Id, string Method) : AiEvent;

public static class AskProviders
{
    /// <summary>The one you ask first if it's ready, else the other if that is; else neither.</summary>
    public static AiProvider? Choose(AiProvider first, AiReadiness claude, AiReadiness codex)
    {
        AiReadiness Of(AiProvider p) => p == AiProvider.Claude ? claude : codex;
        if (Of(first) == AiReadiness.Ready) return first;
        if (Of(Other(first)) == AiReadiness.Ready) return Other(first);
        return null;
    }

    public static AiProvider Other(AiProvider provider) => provider == AiProvider.Claude ? AiProvider.Codex : AiProvider.Claude;

    /// <summary>What the card says when neither's ready: how to get either going.</summary>
    public static string NoneReady(AiReadiness claude, AiReadiness codex)
    {
        var claudeStep = claude == AiReadiness.Missing ? "install Claude Code" : "run `claude` to sign in";
        var codexStep = codex == AiReadiness.Missing ? "install Codex" : "run `codex login`";
        return $"To ask AI, sign in to Claude or Codex: {claudeStep}, or {codexStep}.";
    }

    public static string Name(AiProvider provider) => provider == AiProvider.Claude ? "Claude" : "Codex";
}

/// <summary>How long the last conversation waits to be picked up again (Tab in an empty bar).</summary>
public static class AskRecall
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public static bool CanReopen(DateTimeOffset? lastActivity, DateTimeOffset now) =>
        lastActivity is { } last && now - last <= Window;
}

/// <summary>
/// Claude Code's output in print mode, one JSON object a line ("--output-format stream-json
/// --include-partial-messages"): the answer's text as it streams, what it's doing, and its end.
/// </summary>
public sealed class ClaudeStream
{
    private bool _hadText;

    public IReadOnlyList<AiEvent> Read(string line)
    {
        if (!TryParse(line, out var root)) return [];
        switch (Str(root, "type"))
        {
            case "stream_event" when root.TryGetProperty("event", out var e):
                switch (Str(e, "type"))
                {
                    case "content_block_delta" when e.TryGetProperty("delta", out var delta) && Str(delta, "type") == "text_delta":
                        var text = Str(delta, "text") ?? "";
                        if (text.Length == 0) return [];
                        _hadText = true;
                        return [new AiText(text)];
                    case "content_block_start" when e.TryGetProperty("content_block", out var block):
                        return Str(block, "type") switch
                        {
                            // A new stretch of text after some already (it searched in between, say).
                            "text" when _hadText => [new AiText("\n\n")],
                            "thinking" or "redacted_thinking" => [new AiStatus(AiActivity.Thinking)],
                            "tool_use" or "server_tool_use" when (Str(block, "name") ?? "").Contains("search", StringComparison.OrdinalIgnoreCase)
                                => [new AiStatus(AiActivity.Searching)],
                            _ => [],
                        };
                }
                return [];
            case "result":
                _hadText = false;
                var failed = root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True
                    || Str(root, "subtype") is { } sub && sub.StartsWith("error", StringComparison.Ordinal);
                if (!failed) return [new AiDone()];
                var message = Str(root, "result")
                    ?? (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
                        ? string.Join("; ", errors.EnumerateArray().Select(x => x.ToString())) : null)
                    ?? Str(root, "subtype") ?? "Something went wrong";
                return [AiErrors.Classify(message)];
        }
        return [];
    }

    internal static bool TryParse(string line, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart()[0] != '{') return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            root = doc.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Codex's app server, one JSON-RPC message a line: the answer's text as it streams, what it's
/// doing, the turn's end, and the replies to (and requests from) Drift's side of the protocol.
/// </summary>
public sealed class CodexStream
{
    private string? _lastItem;

    public IReadOnlyList<AiEvent> Read(string line)
    {
        if (!ClaudeStream.TryParse(line, out var root)) return [];
        var method = ClaudeStream.Str(root, "method");
        var hasId = root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number;
        if (method is null)
        {
            if (!hasId) return [];
            var id = idElement.GetInt64();
            if (root.TryGetProperty("error", out var error))
                return [new CodexReply(id, null, ClaudeStream.Str(error, "message") ?? error.ToString())];
            return [new CodexReply(id, root.TryGetProperty("result", out var result) ? result.GetRawText() : null, null)];
        }
        if (hasId) return [new CodexAsked(idElement.GetInt64(), method)];

        var p = root.TryGetProperty("params", out var ps) ? ps : default;
        switch (method)
        {
            case "item/agentMessage/delta":
                var text = ClaudeStream.Str(p, "delta") ?? "";
                var item = ClaudeStream.Str(p, "itemId");
                var events = new List<AiEvent>();
                if (_lastItem is not null && item != _lastItem) events.Add(new AiText("\n\n"));
                _lastItem = item;
                if (text.Length > 0) events.Add(new AiText(text));
                return events;
            case "item/started" when p.ValueKind == JsonValueKind.Object && p.TryGetProperty("item", out var started):
                return ClaudeStream.Str(started, "type") switch
                {
                    "webSearch" => [new AiStatus(AiActivity.Searching)],
                    "reasoning" => [new AiStatus(AiActivity.Thinking)],
                    _ => [],
                };
            case "turn/completed" when p.ValueKind == JsonValueKind.Object && p.TryGetProperty("turn", out var turn):
                _lastItem = null;
                if (ClaudeStream.Str(turn, "status") != "failed") return [new AiDone()];
                var message = turn.TryGetProperty("error", out var err) && ClaudeStream.Str(err, "message") is { } m ? m : "Something went wrong";
                return [AiErrors.Classify(message)];
        }
        return [];
    }
}

public static partial class AiErrors
{
    /// <summary>What kind of trouble an error message from either CLI means.</summary>
    public static AiFailed Classify(string message)
    {
        var lower = message.ToLowerInvariant();
        DateTimeOffset? resets = null;
        // Claude Code: "Claude AI usage limit reached|<unix time it resets>".
        if (ResetSuffix().Match(message) is { Success: true } suffix && long.TryParse(suffix.Groups[1].Value, out var unix))
        {
            resets = DateTimeOffset.FromUnixTimeSeconds(unix);
            message = message[..suffix.Index];
        }
        AiFailure failure =
            lower.Contains("usage limit") || lower.Contains("rate limit") || lower.Contains("429") || lower.Contains("quota")
                || lower.Contains("limit reached") || lower.Contains("hit your limit") ? AiFailure.Limited
            : lower.Contains("401") || lower.Contains("login") || lower.Contains("log in") || lower.Contains("sign in")
                || lower.Contains("api key") || lower.Contains("oauth") || lower.Contains("unauthorized")
                || lower.Contains("authentication") || lower.Contains("not logged in") ? AiFailure.SignedOut
            : lower.Contains("connection") || lower.Contains("error sending request") || lower.Contains("network")
                || lower.Contains("timed out") || lower.Contains("dns") || lower.Contains("offline")
                || lower.Contains("unreachable") ? AiFailure.Unreachable
            : AiFailure.Other;
        return new AiFailed(failure, message.Trim(), resets);
    }

    /// <summary>The line the card shows, in place of the answer.</summary>
    public static string Describe(AiProvider provider, AiFailed failed, TimeZoneInfo zone)
    {
        var name = AskProviders.Name(provider);
        return failed.Failure switch
        {
            AiFailure.Limited when failed.ResetsAt is { } at =>
                $"{name}'s usage limit is reached until {TimeZoneInfo.ConvertTime(at, zone).ToString("h:mm tt", CultureInfo.InvariantCulture)}.",
            AiFailure.Limited => $"{name}'s usage limit is reached.",
            AiFailure.SignedOut => provider == AiProvider.Claude
                ? "Not signed in to Claude. Run `claude` to sign in."
                : "Not signed in to Codex. Run `codex login` to sign in.",
            AiFailure.Unreachable => $"Couldn't reach {name}.",
            _ => $"{name} couldn't answer: {failed.Message}",
        };
    }

    [GeneratedRegex(@"\|(\d{9,11})\s*$")]
    private static partial Regex ResetSuffix();
}

/// <summary>Where Windows keeps things, for finding the CLIs.</summary>
/// <param name="Path">The PATH variable.</param>
public sealed record AiPlaces(string Path, string Home, string LocalAppData, string AppData)
{
    public static AiPlaces Current => new(
        Environment.GetEnvironmentVariable("PATH") ?? "",
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
}

/// <summary>Finds the claude or codex you've installed, wherever its installer put it.</summary>
public static partial class AiLocator
{
    public static string? Find(AiProvider provider, AiPlaces places, Func<string, bool> fileExists, Func<string, IEnumerable<string>> listDirectories)
    {
        var name = provider == AiProvider.Claude ? "claude" : "codex";
        foreach (var dir in places.Path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var clean = dir.Trim('"');
            foreach (var ext in new[] { ".exe", ".cmd" })
            {
                var candidate = System.IO.Path.Combine(clean, name + ext);
                if (fileExists(candidate)) return candidate;
            }
        }

        var known = provider == AiProvider.Claude
            ? new[]
            {
                System.IO.Path.Combine(places.Home, ".local", "bin", "claude.exe"),
                System.IO.Path.Combine(places.AppData, "npm", "claude.cmd"),
            }
            : new[]
            {
                System.IO.Path.Combine(places.LocalAppData, "Programs", "OpenAI", "Codex", "bin", "codex.exe"),
                System.IO.Path.Combine(places.AppData, "npm", "codex.cmd"),
            };
        foreach (var candidate in known)
            if (fileExists(candidate)) return candidate;

        if (provider == AiProvider.Codex)
        {
            // The Codex app's own copy: ~\.codex\packages\standalone\releases\<version>-<target>\bin.
            var releases = System.IO.Path.Combine(places.Home, ".codex", "packages", "standalone", "releases");
            var newest = listDirectories(releases)
                .Select(d => (Dir: d, Version: VersionOf(System.IO.Path.GetFileName(d))))
                .Where(d => d.Version is not null)
                .OrderByDescending(d => d.Version)
                .ThenBy(d => System.IO.Path.GetFileName(d.Dir).StartsWith('.') ? 1 : 0)
                .Select(d => System.IO.Path.Combine(d.Dir, "bin", "codex.exe"))
                .FirstOrDefault(fileExists);
            if (newest is not null) return newest;
        }
        return null;
    }

    public static string? Find(AiProvider provider) =>
        Find(provider, AiPlaces.Current, File.Exists, d =>
        {
            try { return Directory.Exists(d) ? Directory.GetDirectories(d) : []; }
            catch { return []; }
        });

    private static Version? VersionOf(string folder) =>
        VersionPattern().Match(folder) is { Success: true } m && Version.TryParse(m.Value, out var v) ? v : null;

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionPattern();
}

public static class AiNames
{
    /// <summary>"gpt-6-luna" as "GPT-6 Luna", "sonnet" as "Sonnet"; anything unfamiliar as it is.</summary>
    public static string Model(string id)
    {
        if (id is "sonnet" or "opus" or "haiku") return char.ToUpperInvariant(id[0]) + id[1..];
        var parts = id.Split('-');
        if (parts.Length >= 3 && parts[0] == "gpt")
            return $"GPT-{parts[1]} " + string.Join(" ", parts[2..].Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));
        return id;
    }
}

/// <summary>An answer that's still arriving, made presentable as it stands.</summary>
public static class AskReveal
{
    /// <summary>
    /// Bold or code that's been cut off part way, closed for now, so it shows as bold or code
    /// rather than as asterisks and backticks until the rest arrives.
    /// </summary>
    public static string Close(string shown)
    {
        var fences = shown.Split('\n').Count(l => l.TrimStart().StartsWith("```", StringComparison.Ordinal));
        if (fences % 2 == 1) return shown;
        var inCode = false;
        var bold = 0;
        for (var i = 0; i < shown.Length; i++)
        {
            if (shown[i] == '`')
            {
                if (i + 2 < shown.Length && shown[i + 1] == '`' && shown[i + 2] == '`') { i += 2; continue; }
                inCode = !inCode;
            }
            else if (!inCode && shown[i] == '*' && i + 1 < shown.Length && shown[i + 1] == '*')
            {
                bold++;
                i++;
            }
        }
        if (inCode) shown += "`";
        if (bold % 2 == 1) shown += "**";
        return shown;
    }
}
