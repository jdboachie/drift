using System;
using Drift;
using Xunit;

public class LiveScheduleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static LiveSchedule FetchedAt(DateTimeOffset at, bool answered = true, bool doubted = false)
    {
        var schedule = new LiveSchedule();
        schedule.Fetched(at, answered, doubted);
        return schedule;
    }

    [Fact]
    public void The_first_ask_is_straight_away()
    {
        Assert.True(new LiveSchedule().Due(T0, inFront: false));
    }

    [Fact]
    public void In_front_it_asks_every_minute()
    {
        var schedule = FetchedAt(T0);
        Assert.False(schedule.Due(T0.AddSeconds(50), inFront: true));
        Assert.True(schedule.Due(T0.AddMinutes(1), inFront: true));
    }

    [Fact]
    public void Behind_other_windows_every_five()
    {
        var schedule = FetchedAt(T0);
        Assert.False(schedule.Due(T0.AddMinutes(4), inFront: false));
        Assert.True(schedule.Due(T0.AddMinutes(5), inFront: false));
    }

    [Fact]
    public void A_failed_ask_waits_two_minutes_even_in_front()
    {
        var schedule = FetchedAt(T0, answered: false);
        Assert.False(schedule.Due(T0.AddMinutes(1.5), inFront: true));
        Assert.True(schedule.Due(T0.AddMinutes(2), inFront: false));
    }

    [Fact]
    public void An_empty_answer_is_asked_again_within_a_minute_wherever_drift_is()
    {
        var schedule = FetchedAt(T0, doubted: true);
        Assert.True(schedule.Due(T0.AddMinutes(1), inFront: false));
    }

    [Fact]
    public void After_something_done_on_github_it_asks_at_10_30_and_90_seconds()
    {
        var schedule = FetchedAt(T0);
        schedule.Acted(T0);
        Assert.False(schedule.Due(T0.AddSeconds(5), inFront: false));
        Assert.True(schedule.Due(T0.AddSeconds(10), inFront: false));
        schedule.Fetched(T0.AddSeconds(10), answered: true, doubted: false);
        Assert.False(schedule.Due(T0.AddSeconds(20), inFront: false));
        Assert.True(schedule.Due(T0.AddSeconds(30), inFront: false));
        schedule.Fetched(T0.AddSeconds(30), answered: true, doubted: false);
        Assert.False(schedule.Due(T0.AddSeconds(80), inFront: false));
        Assert.True(schedule.Due(T0.AddSeconds(90), inFront: false));
        schedule.Fetched(T0.AddSeconds(90), answered: true, doubted: false);
        // Then back to the usual.
        Assert.False(schedule.Due(T0.AddMinutes(4), inFront: false));
    }

    [Fact]
    public void Follow_ups_missed_while_asking_count_once()
    {
        var schedule = FetchedAt(T0);
        schedule.Acted(T0);
        // One ask that finished after both the 10 and 30 second follow-ups were due.
        schedule.Fetched(T0.AddSeconds(35), answered: true, doubted: false);
        Assert.False(schedule.Due(T0.AddSeconds(60), inFront: false));
        Assert.True(schedule.Due(T0.AddSeconds(90), inFront: false));
    }

    [Fact]
    public void Coming_to_the_front_asks_if_the_last_answer_is_over_30_seconds_old()
    {
        var stale = FetchedAt(T0);
        stale.CameToFront(T0.AddSeconds(31));
        Assert.True(stale.Due(T0.AddSeconds(31), inFront: true));

        var fresh = FetchedAt(T0);
        fresh.CameToFront(T0.AddSeconds(20));
        Assert.False(fresh.Due(T0.AddSeconds(20), inFront: true));
    }

    [Fact]
    public void A_page_on_github_brings_the_next_ask_to_within_a_minute_of_the_last()
    {
        var schedule = FetchedAt(T0);
        schedule.Visited(T0.AddSeconds(20));
        Assert.False(schedule.Due(T0.AddSeconds(50), inFront: false));
        Assert.True(schedule.Due(T0.AddMinutes(1), inFront: false));
    }

    [Fact]
    public void Asked_for_now_it_is_due_now()
    {
        var schedule = FetchedAt(T0);
        schedule.Now();
        Assert.True(schedule.Due(T0.AddSeconds(1), inFront: false));
    }
}
