using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StreamOverlay;

/// <summary>Lethal Company: player, moon, crew, day, quota and ship credits. <c>findObject</c> finds the terminal, which
/// exists only on the ship.</summary>
internal sealed class LethalCompanyProvider(Func<string, Type?> find, Func<Type, object?> findObject) : IOverlayProvider
{
    private static readonly Regex moonNumber = new(@"^\d+\s+", RegexOptions.Compiled);

    public string Game => "lethal-company";

    public Dictionary<string, object?>? Read()
    {
        object? round = Probe.Static(find("StartOfRound"), "Instance");
        object? local = Probe.Member(round, "localPlayerController");
        if (round == null || local == null)
            return null;

        int total = 0;
        int alive = 0;
        foreach (object? player in Probe.Member(round, "allPlayerScripts") as IEnumerable ?? Array.Empty<object>())
        {
            bool controlled = Probe.Member(player, "isPlayerControlled") is true;
            bool dead = Probe.Member(player, "isPlayerDead") is true;
            if (controlled || dead)
                total++;
            if (controlled && !dead)
                alive++;
        }

        object? time = Probe.Static(find("TimeOfDay"), "Instance");
        string? moon = Probe.Member(Probe.Member(round, "currentLevel"), "PlanetName")?.ToString();
        return new Dictionary<string, object?>
        {
            ["playerName"] = Probe.Member(local, "playerUsername")?.ToString(),
            ["moon"] = moon == null ? null : moonNumber.Replace(moon, ""),
            ["crewAlive"] = alive,
            ["crewTotal"] = total,
            ["day"] = Probe.Int(Probe.Member(Probe.Member(round, "gameStats"), "daysSpent")),
            ["quota"] = Probe.Int(Probe.Member(time, "profitQuota")),
            ["quotaProgress"] = Probe.Int(Probe.Member(time, "quotaFulfilled")),
            ["daysLeft"] = Probe.Int(Probe.Member(time, "daysUntilDeadline")),
            ["credits"] = Probe.Int(Probe.Member(find("Terminal") is { } terminal ? findObject(terminal) : null, "groupCredits")),
        };
    }
}
