using Drift;

public class StartupEntryTests
{
    [Fact]
    public void The_command_quotes_the_path()
    {
        Assert.Equal("\"C:\\Program Files\\Drift\\Drift.exe\"", StartupEntry.Command(@"C:\Program Files\Drift\Drift.exe"));
    }

    [Fact]
    public void No_entry_is_off()
    {
        Assert.False(StartupEntry.IsOn(null, null));
        Assert.False(StartupEntry.IsOn("", null));
    }

    [Fact]
    public void An_entry_nobody_turned_off_is_on()
    {
        Assert.True(StartupEntry.IsOn("\"C:\\Drift\\Drift.exe\"", null));
    }

    [Fact]
    public void An_entry_turned_off_in_Task_Manager_is_off()
    {
        // Windows marks a startup app turned off with an odd first byte, and on again with an even one.
        Assert.False(StartupEntry.IsOn("\"C:\\Drift\\Drift.exe\"", [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.True(StartupEntry.IsOn("\"C:\\Drift\\Drift.exe\"", [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.True(StartupEntry.IsOn("\"C:\\Drift\\Drift.exe\"", [6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void An_empty_mark_doesnt_turn_it_off()
    {
        Assert.True(StartupEntry.IsOn("\"C:\\Drift\\Drift.exe\"", []));
    }
}
