using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>Where a sideways swipe over the sidebar goes (ProfileOrder.Swipe).</summary>
public enum ProfileSwipe { None, Previous, Next, Library }

/// <summary>
/// Profiles, as Arc has its Spaces: separate sidebars for the parts of your life (Profiles.cs). Their order, and where
/// switching between them goes. Arc's own model and wording were read from Arc for Windows (1.133); its
/// behaviour from its release notes (2021–2024).
/// </summary>
public static class ProfileOrder
{
    /// <summary>How strongly a Profile's icon is drawn in the switcher: the one you're on full, the others faded, as Arc's.</summary>
    public static double IconStrength(bool current) => current ? 1 : 0.6;

    /// <summary>A Profile's icon in the switcher at the bottom of the sidebar takes this much room.</summary>
    public const double IconWidth = 28;

    /// <summary>Arc's fallback name, as a Profile: "Profile N", N its place, or the first after it that isn't taken.</summary>
    public static string DefaultName(IReadOnlyList<string> names)
    {
        for (var n = names.Count + 1; ; n++)
        {
            var name = $"Profile {n}";
            if (!names.Contains(name)) return name;
        }
    }

    /// <summary>A new Profile goes next to the one you're on, not at the end (Arc, April 2023).</summary>
    public static int NewIndex(int current, int count) => count == 0 ? 0 : current + 1;

    /// <summary>The Profile <paramref name="by"/> along from the one you're on; none past the ends.</summary>
    public static int? Step(int current, int count, int by)
    {
        var to = current + by;
        return to >= 0 && to < count ? to : null;
    }

    /// <summary>Ctrl+1–9: that Profile, if there are that many.</summary>
    public static int? ForNumber(int number, int count) => number >= 1 && number <= count ? number - 1 : null;

    /// <summary>
    /// The next Profile is "one to the right" (Arc's Select Next Space), so fingers moving left bring it in, and
    /// moving right go back one. Before the first Profile is the Library, which that swipe opened before Profiles.
    /// </summary>
    public static ProfileSwipe Swipe(int current, int count, bool fingersRight) =>
        fingersRight
            ? current == 0 ? ProfileSwipe.Library : ProfileSwipe.Previous
            : current + 1 < count ? ProfileSwipe.Next : ProfileSwipe.None;

    /// <summary>
    /// After a Profile is deleted, the place in the list (without it) of the Profile you're on: the same one, or
    /// for the one you were on, the one before it.
    /// </summary>
    public static int AfterDelete(int deleted, int current) =>
        deleted < current ? current - 1
        : deleted == current ? System.Math.Max(0, current - 1)
        : current;

    /// <summary>
    /// A swipe between Profiles: how far the sidebar has gone with the fingers (<paramref name="moved"/>, fingers right
    /// positive). Towards a Profile there is (<paramref name="direction"/>, +1 for fingers right) it follows them; any
    /// other way, past the first or last Profile or back past where it started, it stays put (no rubber band: the user
    /// didn't like one, 2026-11-24). It goes no further than the next Profile's place, a <paramref name="page"/> along.
    /// </summary>
    public static double Follow(double moved, int direction, bool canGo, double page) =>
        canGo && System.Math.Sign(moved) == direction ? System.Math.Clamp(moved, -page, page) : 0;

    /// <summary>Profiles page past a little apart, as Arc's Spaces do: this much sidebar shows between one and the next.</summary>
    public const double PageGap = 16;

    /// <summary>From one Profile's page to the next: the sidebar's width and the gap.</summary>
    public static double PageStep(double width) => width + PageGap;

    /// <summary>Where a Profile's sidebar sits on the strip of them all (ProfilePages.cs): a page along for each before it.</summary>
    public static double PageLeft(int index, double width) => index * PageStep(width);

    /// <summary>Where the strip is with a Profile's sidebar in view: moved back as far as that sidebar is along it.</summary>
    public static double StripAt(int index, double width) => -PageLeft(index, width);

    /// <summary>
    /// How long a critically damped spring of this perceptual <paramref name="duration"/> takes to come within half a
    /// pixel of rest and stay there, from <paramref name="offset"/> away at <paramref name="velocity"/> (pixels a
    /// second, the same way as the offset): x(t) = (x₀ + (v₀ + ωx₀)t)e^(−ωt), ω = 2π/duration.
    /// </summary>
    public static double SettleSeconds(double offset, double velocity, double duration)
    {
        var omega = 2 * System.Math.PI / duration;
        var last = 0.0;
        for (var t = 0.0; t <= 3; t += 0.005)
        {
            var x = (offset + (velocity + omega * offset) * t) * System.Math.Exp(-omega * t);
            if (System.Math.Abs(x) >= 0.5) last = t + 0.005;
        }
        return last;
    }

    /// <summary>
    /// Where a critically damped spring of this perceptual <paramref name="duration"/> has got to, <paramref name="seconds"/>
    /// after it left <paramref name="offset"/> from its end at <paramref name="velocity"/>, and how fast it's going: to
    /// catch a Profile's sidebar mid-slide from where it's drawn (the compositor moves it; the window can't ask).
    /// </summary>
    public static (double Offset, double Velocity) SpringAt(double offset, double velocity, double seconds, double duration)
    {
        var omega = 2 * System.Math.PI / duration;
        var b = velocity + omega * offset;
        var decay = System.Math.Exp(-omega * seconds);
        return ((offset + b * seconds) * decay, (velocity - omega * b * seconds) * decay);
    }

    /// <summary>
    /// Follow, for the strip picked up mid-slide <paramref name="carried"/> off its place: from there with the fingers,
    /// towards a Profile no further than that Profile's place, and past an end only back towards where it rests.
    /// </summary>
    public static double FollowFrom(double carried, double moved, int direction, bool canGo, double page)
    {
        var (low, high) = FollowRange(carried, direction, canGo, page);
        return System.Math.Clamp(carried + moved, low, high);
    }

    /// <summary>How far either way FollowFrom lets the strip go off its place.</summary>
    public static (double Low, double High) FollowRange(double carried, int direction, bool canGo, double page) =>
        !canGo ? (System.Math.Min(carried, 0), System.Math.Max(carried, 0))
        : direction > 0 ? (System.Math.Min(carried, 0), page)
        : (-page, System.Math.Max(carried, 0));

    /// <summary>
    /// Let go: on to the Profile if it was flicked that way (LibrarySwipe.FlickSpeed), or let go past a quarter of the
    /// way there (LibrarySwipe.Commit); otherwise back. Always back with no Profile that way.
    /// </summary>
    public static bool Commits(double moved, double speed, double width, int direction, bool canGo) =>
        canGo && (System.Math.Abs(speed) >= LibrarySwipe.FlickSpeed
            ? System.Math.Sign(speed) == direction
            : System.Math.Sign(moved) == direction && System.Math.Abs(moved) >= width * LibrarySwipe.Commit);

    /// <summary>More Profiles than their icons have room for show as dots.</summary>
    public static bool Dots(int count, double room) => count * IconWidth > room;
}
