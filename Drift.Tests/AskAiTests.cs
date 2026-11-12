using Drift;

public class AskProvidersTests
{
    [Fact]
    public void The_one_you_ask_first_when_it_is_ready()
    {
        Assert.Equal(AiProvider.Claude, AskProviders.Choose(AiProvider.Claude, AiReadiness.Ready, AiReadiness.Ready));
        Assert.Equal(AiProvider.Codex, AskProviders.Choose(AiProvider.Codex, AiReadiness.Ready, AiReadiness.Ready));
    }

    [Fact]
    public void Otherwise_the_other_one()
    {
        Assert.Equal(AiProvider.Codex, AskProviders.Choose(AiProvider.Claude, AiReadiness.SignedOut, AiReadiness.Ready));
        Assert.Equal(AiProvider.Claude, AskProviders.Choose(AiProvider.Codex, AiReadiness.Ready, AiReadiness.Missing));
    }

    [Fact]
    public void Neither_ready_is_none()
    {
        Assert.Null(AskProviders.Choose(AiProvider.Claude, AiReadiness.SignedOut, AiReadiness.Missing));
    }

    [Fact]
    public void Neither_ready_says_how_to_sign_in_to_one()
    {
        var text = AskProviders.NoneReady(AiReadiness.SignedOut, AiReadiness.Missing);
        Assert.Contains("claude", text);
        Assert.DoesNotContain("codex login", text);
        Assert.Contains("codex login", AskProviders.NoneReady(AiReadiness.Missing, AiReadiness.SignedOut));
    }
}

public class AskRecallTests
{
    private static readonly DateTimeOffset Then = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Within_five_minutes_the_last_conversation_comes_back()
    {
        Assert.True(AskRecall.CanReopen(Then, Then.AddMinutes(4.9)));
    }

    [Fact]
    public void After_that_it_does_not()
    {
        Assert.False(AskRecall.CanReopen(Then, Then.AddMinutes(5.1)));
        Assert.False(AskRecall.CanReopen(null, Then));
    }
}

public class ClaudeStreamTests
{
    private static string Delta(string text) =>
        "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":"
        + System.Text.Json.JsonSerializer.Serialize(text) + "}}}";

    private static string BlockStart(string type, string? name = null) =>
        "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"" + type + "\""
        + (name is null ? "" : ",\"name\":\"" + name + "\"") + "}}}";

    [Fact]
    public void Text_comes_through_as_it_streams()
    {
        var stream = new ClaudeStream();
        Assert.Equal(new AiText("Acc"), Assert.Single(stream.Read(Delta("Acc"))));
        Assert.Equal(new AiText("ra"), Assert.Single(stream.Read(Delta("ra"))));
    }

    [Fact]
    public void A_second_stretch_of_text_in_one_answer_starts_a_new_paragraph()
    {
        var stream = new ClaudeStream();
        stream.Read(BlockStart("text"));
        stream.Read(Delta("Let me look."));
        stream.Read(BlockStart("tool_use", "WebSearch"));
        Assert.Equal(new AiEvent[] { new AiText("\n\n") }, stream.Read(BlockStart("text")));
    }

    [Fact]
    public void Searching_and_thinking_are_reported()
    {
        var stream = new ClaudeStream();
        Assert.Equal(new AiStatus(AiActivity.Searching), Assert.Single(stream.Read(BlockStart("tool_use", "WebSearch"))));
        Assert.Equal(new AiStatus(AiActivity.Thinking), Assert.Single(stream.Read(BlockStart("thinking"))));
    }

    [Fact]
    public void The_result_ends_the_answer()
    {
        var stream = new ClaudeStream();
        Assert.Equal(new AiDone(), Assert.Single(stream.Read("""{"type":"result","subtype":"success","is_error":false,"result":"Accra"}""")));
    }

    [Fact]
    public void Each_answer_starts_fresh()
    {
        var stream = new ClaudeStream();
        stream.Read(Delta("one"));
        stream.Read("""{"type":"result","subtype":"success","is_error":false}""");
        Assert.Empty(stream.Read(BlockStart("text")));
    }

    [Fact]
    public void A_failed_result_is_a_failure()
    {
        var stream = new ClaudeStream();
        var failed = Assert.IsType<AiFailed>(Assert.Single(stream.Read(
            """{"type":"result","subtype":"success","is_error":true,"result":"Invalid API key · Please run /login"}""")));
        Assert.Equal(AiFailure.SignedOut, failed.Failure);
    }

    [Fact]
    public void Everything_else_is_ignored()
    {
        var stream = new ClaudeStream();
        Assert.Empty(stream.Read("""{"type":"system","subtype":"init"}"""));
        Assert.Empty(stream.Read("not json"));
        Assert.Empty(stream.Read(""));
    }
}

public class CodexStreamTests
{
    [Fact]
    public void Text_comes_through_as_it_streams()
    {
        var stream = new CodexStream();
        Assert.Equal(new AiText("Accra"), Assert.Single(stream.Read(
            """{"method":"item/agentMessage/delta","params":{"threadId":"t","turnId":"u","itemId":"a","delta":"Accra"}}""")));
    }

    [Fact]
    public void A_second_message_in_one_answer_starts_a_new_paragraph()
    {
        var stream = new CodexStream();
        stream.Read("""{"method":"item/agentMessage/delta","params":{"itemId":"a","delta":"Looking."}}""");
        Assert.Equal(new AiEvent[] { new AiText("\n\n"), new AiText("Found") },
            stream.Read("""{"method":"item/agentMessage/delta","params":{"itemId":"b","delta":"Found"}}"""));
    }

    [Fact]
    public void Searching_and_thinking_are_reported()
    {
        var stream = new CodexStream();
        Assert.Equal(new AiStatus(AiActivity.Searching), Assert.Single(stream.Read(
            """{"method":"item/started","params":{"item":{"type":"webSearch","id":"w"}}}""")));
        Assert.Equal(new AiStatus(AiActivity.Thinking), Assert.Single(stream.Read(
            """{"method":"item/started","params":{"item":{"type":"reasoning","id":"r"}}}""")));
    }

    [Fact]
    public void A_completed_or_stopped_turn_ends_the_answer()
    {
        var stream = new CodexStream();
        Assert.Equal(new AiDone(), Assert.Single(stream.Read(
            """{"method":"turn/completed","params":{"threadId":"t","turn":{"id":"u","status":"completed","error":null}}}""")));
        Assert.Equal(new AiDone(), Assert.Single(stream.Read(
            """{"method":"turn/completed","params":{"threadId":"t","turn":{"id":"u","status":"interrupted","error":null}}}""")));
    }

    [Fact]
    public void A_failed_turn_is_a_failure()
    {
        var stream = new CodexStream();
        var failed = Assert.IsType<AiFailed>(Assert.Single(stream.Read(
            """{"method":"turn/completed","params":{"turn":{"status":"failed","error":{"message":"unexpected status 401 Unauthorized: Missing bearer or basic authentication in header"}}}}""")));
        Assert.Equal(AiFailure.SignedOut, failed.Failure);
    }

    [Fact]
    public void Answers_to_requests_and_the_server_asking_for_approval()
    {
        var stream = new CodexStream();
        Assert.Equal(new CodexReply(4, """{"thread":{"id":"t"}}""", null),
            Assert.Single(stream.Read("""{"id":4,"result":{"thread":{"id":"t"}}}""")));
        Assert.Equal(new CodexReply(5, null, "bad"),
            Assert.Single(stream.Read("""{"id":5,"error":{"code":-1,"message":"bad"}}""")));
        Assert.Equal(new CodexAsked(9, "item/commandExecution/requestApproval"),
            Assert.Single(stream.Read("""{"id":9,"method":"item/commandExecution/requestApproval","params":{}}""")));
    }

    [Fact]
    public void Retries_and_everything_else_are_ignored()
    {
        var stream = new CodexStream();
        Assert.Empty(stream.Read("""{"method":"error","params":{"error":{"message":"Reconnecting... 2/5"},"willRetry":true}}"""));
        Assert.Empty(stream.Read("""{"method":"thread/started","params":{}}"""));
        Assert.Empty(stream.Read("garbage"));
    }
}

public class AiErrorsTests
{
    [Theory]
    [InlineData("Claude AI usage limit reached|1794255000", AiFailure.Limited)]
    [InlineData("You've hit your usage limit. Upgrade to Pro or try again at 3:40 PM.", AiFailure.Limited)]
    [InlineData("unexpected status 429 Too Many Requests", AiFailure.Limited)]
    [InlineData("Invalid API key · Please run /login", AiFailure.SignedOut)]
    [InlineData("OAuth token has expired. Please obtain a new token or refresh your existing token.", AiFailure.SignedOut)]
    [InlineData("unexpected status 401 Unauthorized", AiFailure.SignedOut)]
    [InlineData("error sending request for url (https://api.openai.com/v1/responses)", AiFailure.Unreachable)]
    [InlineData("API Error: Connection error.", AiFailure.Unreachable)]
    [InlineData("Something odd", AiFailure.Other)]
    public void Kinds_of_failure(string message, AiFailure expected)
    {
        Assert.Equal(expected, AiErrors.Classify(message).Failure);
    }

    [Fact]
    public void A_usage_limit_says_when_it_resets()
    {
        var failed = AiErrors.Classify("Claude AI usage limit reached|1794255000");
        var text = AiErrors.Describe(AiProvider.Claude, failed, TimeZoneInfo.Utc);
        Assert.Contains("Claude", text);
        Assert.Contains("limit", text);
        Assert.Contains("8:10 PM", text); // 1794255000 is 20:10 UTC
    }

    [Fact]
    public void Signed_out_says_what_to_run()
    {
        Assert.Contains("codex login", AiErrors.Describe(AiProvider.Codex, new AiFailed(AiFailure.SignedOut, "401"), TimeZoneInfo.Utc));
        Assert.Contains("`claude`", AiErrors.Describe(AiProvider.Claude, new AiFailed(AiFailure.SignedOut, "401"), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Anything_else_shows_what_went_wrong()
    {
        Assert.Contains("Something odd", AiErrors.Describe(AiProvider.Claude, new AiFailed(AiFailure.Other, "Something odd"), TimeZoneInfo.Utc));
    }
}

public class AiLocatorTests
{
    private const string Home = @"C:\Users\me";
    private const string Local = @"C:\Users\me\AppData\Local";
    private const string Roaming = @"C:\Users\me\AppData\Roaming";

    private static AiPlaces Places(string path = "") => new(path, Home, Local, Roaming);

    [Fact]
    public void Claude_on_the_path_first()
    {
        var found = AiLocator.Find(AiProvider.Claude, Places(@"C:\tools;C:\bin"),
            f => f is @"C:\bin\claude.exe" or @"C:\Users\me\.local\bin\claude.exe", _ => []);
        Assert.Equal(@"C:\bin\claude.exe", found);
    }

    [Fact]
    public void Claude_from_its_installer_or_npm()
    {
        Assert.Equal(@"C:\Users\me\.local\bin\claude.exe",
            AiLocator.Find(AiProvider.Claude, Places(), f => f == @"C:\Users\me\.local\bin\claude.exe", _ => []));
        Assert.Equal(@"C:\Users\me\AppData\Roaming\npm\claude.cmd",
            AiLocator.Find(AiProvider.Claude, Places(), f => f == @"C:\Users\me\AppData\Roaming\npm\claude.cmd", _ => []));
    }

    [Fact]
    public void Codex_from_its_installer()
    {
        Assert.Equal(@"C:\Users\me\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe",
            AiLocator.Find(AiProvider.Codex, Places(), f => f == @"C:\Users\me\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe", _ => []));
    }

    [Fact]
    public void Codex_from_the_newest_package_the_desktop_app_left()
    {
        var releases = @"C:\Users\me\.codex\packages\standalone\releases";
        var dirs = new[] { $@"{releases}\0.99.0-x86_64-pc-windows-msvc", $@"{releases}\.staging.0.146.0-x86_64-pc-windows-msvc.24616" };
        var found = AiLocator.Find(AiProvider.Codex, Places(),
            f => f.EndsWith(@"\bin\codex.exe") && f.StartsWith(releases), d => d == releases ? dirs : []);
        Assert.Equal($@"{dirs[1]}\bin\codex.exe", found);
    }

    [Fact]
    public void Nowhere_is_null()
    {
        Assert.Null(AiLocator.Find(AiProvider.Codex, Places(@"C:\bin"), _ => false, _ => []));
    }
}

public class AiNamesTests
{
    [Fact]
    public void Model_names_as_people_say_them()
    {
        Assert.Equal("Sonnet", AiNames.Model("sonnet"));
        Assert.Equal("Opus", AiNames.Model("opus"));
        Assert.Equal("GPT-6 Luna", AiNames.Model("gpt-6-luna"));
        Assert.Equal("GPT-5.6 Sol", AiNames.Model("gpt-5.6-sol"));
        Assert.Equal("something-new", AiNames.Model("something-new"));
    }
}

public class AskRevealTests
{
    [Fact]
    public void Bold_or_code_cut_off_part_way_shows_as_bold_or_code_already()
    {
        Assert.Equal("so **bol**", AskReveal.Close("so **bol"));
        Assert.Equal("run `dotnet`", AskReveal.Close("run `dotnet"));
        Assert.Equal("**done** here", AskReveal.Close("**done** here"));
        // Inside a code block, nothing's touched.
        Assert.Equal("```\nx = `a", AskReveal.Close("```\nx = `a"));
    }
}
