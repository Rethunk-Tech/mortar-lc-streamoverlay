using StreamOverlay;
using Xunit;

namespace StreamOverlay.Tests;

public class LethalCompanyOverlayTests
{
    private sealed class Level { public readonly string PlanetName = "41 Experimentation"; }
    private sealed class Stats { public readonly int daysSpent = 6; }
    private sealed class FakePlayer
    {
        public readonly string playerUsername = "Zed";
        public bool isPlayerControlled;
        public bool isPlayerDead;
    }
    private sealed class FakeRound
    {
        public static FakeRound? Instance;
        public readonly FakePlayer localPlayerController = new() { isPlayerControlled = true };
        public FakePlayer[] allPlayerScripts = [];
        public readonly Level currentLevel = new();
        public readonly Stats gameStats = new();
    }
    private sealed class FakeTime
    {
        public static readonly FakeTime Instance = new();
        public readonly int profitQuota = 400;
        public readonly int quotaFulfilled = 150;
        public readonly int daysUntilDeadline = 2;
    }
    private sealed class FakeTerminal { public readonly int groupCredits = 90; }

    private static Type? Find(string name) => name switch
    {
        "StartOfRound" => typeof(FakeRound),
        "TimeOfDay" => typeof(FakeTime),
        "Terminal" => typeof(FakeTerminal),
        _ => null,
    };

    [Fact]
    public void LethalCompanyReadsCrewMoonQuotaAndCredits()
    {
        FakeRound.Instance = new FakeRound
        {
            allPlayerScripts = [new() { isPlayerControlled = true }, new() { isPlayerDead = true }, new()],
        };
        IOverlayProvider provider = new LethalCompanyProvider(Find, _ => new FakeTerminal());
        string json = OverlayProviders.Snapshot(provider, ex => throw ex);
        Assert.Equal(
            "{\"inGame\":true,\"game\":\"lethal-company\",\"playerName\":\"Zed\",\"moon\":\"Experimentation\",\"crewAlive\":1,\"crewTotal\":2,\"day\":6,\"quota\":400,\"quotaProgress\":150,\"daysLeft\":2,\"credits\":90}",
            json);
    }

    [Fact]
    public void LethalCompanyIsNotInGameWithoutARound()
    {
        FakeRound.Instance = null;
        IOverlayProvider provider = new LethalCompanyProvider(Find, _ => null);
        Assert.Equal(OverlayServer.NotInGame, OverlayProviders.Snapshot(provider, ex => throw ex));
    }
}
