using System;
using System.Collections.Generic;

namespace StreamOverlay;

/// <summary>One game's overlay values. The HTTP server and the poll loop are shared; a provider only reads its game.</summary>
internal interface IOverlayProvider
{
    /// <summary>Mortar's id for the game, sent as <c>game</c> in /state so the page picks its layout.</summary>
    string Game { get; }

    /// <summary>The current values, or null when the player is not in a session. Called on Unity's main thread only.</summary>
    Dictionary<string, object?>? Read();
}

/// <summary>Turns a provider's values into the overlay's JSON.</summary>
internal static class OverlayProviders
{
    /// <summary>The JSON for the provider's current values; a provider that throws reads as not in game.</summary>
    public static string Snapshot(IOverlayProvider provider, Action<Exception> onError)
    {
        Dictionary<string, object?>? values;
        try
        {
            values = provider.Read();
        }
        catch (Exception ex)
        {
            onError(ex);
            return OverlayServer.NotInGame;
        }
        if (values == null)
            return OverlayServer.NotInGame;
        var all = new Dictionary<string, object?> { ["inGame"] = true, ["game"] = provider.Game };
        foreach (KeyValuePair<string, object?> pair in values)
            all[pair.Key] = pair.Value;
        return OverlayJson.Write(all);
    }
}
