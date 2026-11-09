using StreamOverlay;
using Xunit;

namespace StreamOverlay.Tests;

public class OverlayTests
{
    [Fact]
    public void ConfigNeedsEnabledPortAndToken()
    {
        const string token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var on = OverlayConfig.Parse($"{{\"OverlayEnabled\":true,\"OverlayPort\":8123,\"OverlayToken\":\"{token}\",\"StartupProfile\":false}}");
        Assert.Equal(8123, on?.Port);
        Assert.Equal(token, on?.Token);
        Assert.Null(OverlayConfig.Parse("{\"OverlayEnabled\":true,\"OverlayPort\":8123,\"OverlayToken\":\"ab12\"}"));
        Assert.Null(OverlayConfig.Parse($"{{\"OverlayEnabled\":false,\"OverlayPort\":8123,\"OverlayToken\":\"{token}\"}}"));
        Assert.Null(OverlayConfig.Parse($"{{\"OverlayEnabled\":true,\"OverlayPort\":70000,\"OverlayToken\":\"{token}\"}}"));
        Assert.Null(OverlayConfig.Parse($"{{\"OverlayEnabled\":true,\"OverlayPort\":0,\"OverlayToken\":\"{token}\"}}"));
        Assert.Null(OverlayConfig.Parse("{\"OverlayEnabled\":true,\"OverlayPort\":8123,\"OverlayToken\":\"\"}"));
        Assert.Null(OverlayConfig.Parse(null));
    }

    [Fact]
    public void EnabledOverlayWithABadPortOrMissingTokenNamesTheProblem()
    {
        const string token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        Assert.Null(OverlayConfig.InvalidWhenEnabled(null));
        Assert.Null(OverlayConfig.InvalidWhenEnabled($"{{\"OverlayEnabled\":false,\"OverlayPort\":8123,\"OverlayToken\":\"{token}\"}}"));
        Assert.Null(OverlayConfig.InvalidWhenEnabled($"{{\"OverlayEnabled\":true,\"OverlayPort\":8123,\"OverlayToken\":\"{token}\"}}"));
        Assert.Equal(
            "overlay.json OverlayToken is too short",
            OverlayConfig.InvalidWhenEnabled("{\"OverlayEnabled\":true,\"OverlayPort\":8123,\"OverlayToken\":\"ab12\"}"));
        Assert.Equal(
            "overlay.json is enabled but OverlayPort or OverlayToken is missing",
            OverlayConfig.InvalidWhenEnabled("{\"OverlayEnabled\":true,\"OverlayPort\":8123}"));
        Assert.Equal(
            "overlay.json OverlayPort is not a TCP port",
            OverlayConfig.InvalidWhenEnabled($"{{\"OverlayEnabled\":true,\"OverlayPort\":70000,\"OverlayToken\":\"{token}\"}}"));
        Assert.Equal(
            "overlay.json OverlayPort is not a TCP port",
            OverlayConfig.InvalidWhenEnabled($"{{\"OverlayEnabled\":true,\"OverlayPort\":0,\"OverlayToken\":\"{token}\"}}"));
    }

    [Fact]
    public void IntReadsTheIntegerWidthsAGameFieldUses()
    {
        Assert.Equal(3, Probe.Int(3));
        Assert.Equal(4, Probe.Int(4u));
        Assert.Equal(5, Probe.Int((short)5));
        Assert.Equal(6, Probe.Int((ushort)6));
        Assert.Equal(7, Probe.Int((byte)7));
        Assert.Equal(-8, Probe.Int((sbyte)-8));
        Assert.Equal(9, Probe.Int(9L));
        Assert.Equal(10, Probe.Int(10f));
        Assert.Equal(11, Probe.Int(11.9));
        Assert.Null(Probe.Int("3"));
    }

    [Fact]
    public void JsonLeavesOutNullsAndEscapes()
    {
        var values = new Dictionary<string, object?> { ["a"] = "x\"y", ["b"] = null, ["c"] = 3, ["d"] = true, ["e"] = new[] { "p", "q" } };
        Assert.Equal("{\"a\":\"x\\\"y\",\"c\":3,\"d\":true,\"e\":[\"p\",\"q\"]}", OverlayJson.Write(values));
    }

    [Fact]
    public void JsonWritesFalseDoublesLongsAndSkipsNullListItems()
    {
        var values = new Dictionary<string, object?>
        {
            ["a"] = false,
            ["b"] = 1.5,
            ["c"] = 4L,
            ["d"] = new object?[] { null, "x" },
            ["e"] = DayOfWeek.Sunday,
        };
        Assert.Equal("{\"a\":false,\"b\":1.5,\"c\":4,\"d\":[\"x\"],\"e\":\"Sunday\"}", OverlayJson.Write(values));
    }

    private sealed class Throws : IOverlayProvider
    {
        public string Game => "x";
        public Dictionary<string, object?>? Read() => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void AProviderThatThrowsReadsAsNotInGame()
    {
        string? seen = null;
        Assert.Equal(OverlayServer.NotInGame, OverlayProviders.Snapshot(new Throws(), ex => seen = ex.Message));
        Assert.Equal("boom", seen);
    }

    [Theory]
    [InlineData("GET /state?token=t HTTP/1.1", null, 200)]
    [InlineData("GET /state?token=%74 HTTP/1.1", null, 200)]
    [InlineData("GET /state?foo=1&token=t HTTP/1.1", null, 200)]
    [InlineData("GET /state HTTP/1.1", "Bearer t", 200)]
    [InlineData("GET /state HTTP/1.1", "bearer t", 200)]
    [InlineData("GET /state?token=nope HTTP/1.1", null, 401)]
    [InlineData("GET /state HTTP/1.1", null, 401)]
    [InlineData("GET /other?token=t HTTP/1.1", null, 404)]
    [InlineData("POST /state?token=t HTTP/1.1", null, 405)]
    [InlineData("OPTIONS /state HTTP/1.1", null, 204)]
    [InlineData("OPTIONS /state?token=t HTTP/1.1", null, 204)]
    [InlineData("OPTIONS /state HTTP/1.1", "Bearer nope", 204)]
    [InlineData("HEAD /state?token=t HTTP/1.1", null, 200)]
    [InlineData("HEAD /state HTTP/1.1", "Bearer t", 200)]
    [InlineData("HEAD /state HTTP/1.1", null, 401)]
    [InlineData("garbage", null, 400)]
    [InlineData("GET /state?token=% HTTP/1.1", null, 400)]
    [InlineData("GET /state?token=%2 HTTP/1.1", null, 400)]
    public void ServerChecksMethodPathAndToken(string request, string? auth, int status)
    {
        using var server = new OverlayServer(0, "t");
        server.Set("{\"inGame\":false}");
        var headers = new Dictionary<string, string>();
        if (auth != null)
            headers["Authorization"] = auth;
        string reply = server.Answer(request, headers);
        Assert.StartsWith($"HTTP/1.1 {status} ", reply);
        if (status == 200 && request.StartsWith("GET ", StringComparison.Ordinal))
            Assert.EndsWith("{\"inGame\":false}", reply);
        if (status == 200 && request.StartsWith("HEAD ", StringComparison.Ordinal))
            Assert.EndsWith("\r\n\r\n", reply);
        if (status == 204)
        {
            Assert.Contains("Access-Control-Allow-Origin: *", reply);
            Assert.Contains("Access-Control-Allow-Methods: GET, HEAD, OPTIONS", reply);
            Assert.Contains("Access-Control-Allow-Headers: Authorization", reply);
            Assert.EndsWith("\r\n\r\n", reply);
        }
    }

    [Fact]
    public void QueryTokenTurnsPlusIntoASpace()
    {
        using var server = new OverlayServer(0, "a b");
        server.Set("{\"inGame\":false}");
        string reply = server.Answer("GET /state?token=a+b HTTP/1.1", new Dictionary<string, string>());
        Assert.StartsWith("HTTP/1.1 200 ", reply);
    }

    [Fact]
    public void EndlessHeadersAreCutOff()
    {
        using var server = new OverlayServer(0, "t");
        var sb = new System.Text.StringBuilder("GET /state HTTP/1.1\r\n");
        for (int i = 0; i < 100000; i++)
            sb.Append("X-").Append(i).Append(": v\r\n");
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(sb.ToString()));
        Assert.StartsWith("HTTP/1.1 431 ", server.Respond(stream));
        Assert.True(stream.Position < stream.Length / 10);
    }
}
