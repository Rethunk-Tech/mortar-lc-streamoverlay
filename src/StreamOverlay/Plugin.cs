using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace StreamOverlay;

[BepInPlugin(MyPluginInfo.Id, MyPluginInfo.Name, MyPluginInfo.Version)]
[BepInProcess("Lethal Company.exe")]
public class Plugin : BaseUnityPlugin
{
    private const int MaxTextBytes = 65536;

    private readonly Dictionary<Type, UnityEngine.Object> found = [];

    private OverlayServer? server;

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        Log.LogInfo($"{MyPluginInfo.Name} {MyPluginInfo.Version} loaded");

        string? json = ReadProfileFile(OverlayConfig.File);
        var config = OverlayConfig.Parse(json);
        if (config == null)
        {
            if (OverlayConfig.InvalidWhenEnabled(json) is { } why)
                Log.LogWarning($"Stream overlay is off: {why}.");
            return;
        }

        OverlayServer created;
        try
        {
            created = new OverlayServer(config.Port, config.Token);
            created.Start();
        }
        catch (SocketException ex)
        {
            Log.LogError($"Stream overlay is off: could not bind 127.0.0.1:{config.Port}: {ex.Message}");
            return;
        }

        server = created;
        OverlayRunner.Start(created, new LethalCompanyProvider(Probe.Find, FindObject), Log);
        // Only a real quit stops the server: the first scene load destroys the manager object and this component with
        // it, while the game runs on.
        Application.quitting += Shutdown;
        Log.LogInfo($"Stream overlay on 127.0.0.1:{config.Port}.");
    }

    private void Shutdown()
    {
        server?.Dispose();
        server = null;
    }

    // Looks a scene object up once and again only after Unity has destroyed it.
    private object? FindObject(Type type)
    {
        if (!found.TryGetValue(type, out UnityEngine.Object? value) || !value)
        {
            value = UnityEngine.Object.FindObjectOfType(type);
            if (value)
                found[type] = value;
            else
                found.Remove(type);
        }
        return value ? value : null;
    }

    // Mortar leaves its request in the profile folder that holds BepInEx's, which a Steam launch reaches where an
    // environment variable would not.
    private static string? ReadProfileFile(string relative)
    {
        string profile = Path.GetDirectoryName(Paths.BepInExRootPath) ?? "";
        string? text = ReadText(Path.Join(profile, relative), out string? error);
        if (error != null)
            Log.LogWarning($"Could not read {relative}: {error}");
        return text;
    }

    /// <summary>The file's text, or null when it is missing. <paramref name="error"/> is set only when the file exists
    /// and cannot be read.</summary>
    private static string? ReadText(string path, out string? error)
    {
        error = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxTextBytes)
            {
                error = $"file exceeds {MaxTextBytes} bytes";
                return null;
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }
}
