using System;
using BepInEx.Logging;
using UnityEngine;

namespace StreamOverlay;

/// <summary>Reads the provider on Unity's main thread twice a second and hands the server the finished JSON. It lives on
/// an object of its own because the plugin's object does not survive the first scene load.</summary>
internal sealed class OverlayRunner : MonoBehaviour
{
    private const float IntervalSeconds = 0.5f;

    private OverlayServer? server;
    private IOverlayProvider? provider;
    private ManualLogSource? log;
    private float next;
    private string lastError = "";

    public static void Start(OverlayServer server, IOverlayProvider provider, ManualLogSource log)
    {
        var host = new GameObject("StreamOverlay") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(host);
        OverlayRunner runner = host.AddComponent<OverlayRunner>();
        runner.server = server;
        runner.provider = provider;
        runner.log = log;
    }

    private void Update()
    {
        if (Time.unscaledTime < next || server == null || provider == null)
            return;
        next = Time.unscaledTime + IntervalSeconds;
        server.Set(OverlayProviders.Snapshot(provider, ex =>
        {
            if (ex.Message != lastError)
                log?.LogWarning($"Overlay read failed: {ex.Message}");
            lastError = ex.Message;
        }));
    }
}
