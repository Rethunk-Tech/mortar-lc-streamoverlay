using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace StreamOverlay;

/// <summary>Loopback-only HTTP: <c>GET /state</c> answers the last snapshot as JSON, behind a token in an
/// <c>Authorization: Bearer</c> header or a <c>token</c> query parameter. It reads nothing from the game; the poll loop
/// hands it finished JSON.</summary>
internal sealed class OverlayServer(int port, string token) : IDisposable
{
    public const string NotInGame = "{\"inGame\":false}";

    private const int MaxLineBytes = 4096;
    private const int MaxHeaderLines = 64;
    private const int MaxClients = 8;
    private const int ClientTimeoutMs = 5000;
    // A peer that has not finished its request line by now is not a viewer.
    private const int UnauthTimeoutMs = 250;

    private readonly TcpListener listener = new(IPAddress.Loopback, port);
    private readonly string token = token;
    private volatile string snapshot = NotInGame;
    private int active;
    private volatile bool stopped;

    public void Set(string json) => snapshot = json;

    public void Start()
    {
        listener.Start();
        new Thread(AcceptLoop) { IsBackground = true, Name = "StreamOverlay" }.Start();
    }

    public void Dispose()
    {
        stopped = true;
        listener.Stop();
    }

    internal string Respond(Stream stream)
    {
        string? request = ReadLine(stream, MaxLineBytes);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        int lines = 0;
        while (request != null && (line = ReadLine(stream, MaxLineBytes)) is { Length: > 0 })
        {
            if (++lines > MaxHeaderLines)
                return Response(431, "{\"error\":\"too many headers\"}");
            int colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return Answer(request, headers);
    }

    internal string Answer(string? request, IReadOnlyDictionary<string, string> headers)
    {
        string[] parts = (request ?? "").Split([' '], 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return Response(400, "{\"error\":\"bad request\"}");
        string method = parts[0];
        if (method is not ("GET" or "HEAD" or "OPTIONS"))
            return Response(405, "{\"error\":\"method not allowed\"}");
        string target = parts[1];
        int query = target.IndexOf('?');
        if ((query < 0 ? target : target[..query]) != "/state")
            return Response(404, "{\"error\":\"not found\"}");
        if (method == "OPTIONS")
            return Response(204, "", "Access-Control-Allow-Methods: GET, HEAD, OPTIONS\r\nAccess-Control-Allow-Headers: Authorization\r\n");
        string? given;
        try
        {
            given = TokenOf(query < 0 ? "" : target[(query + 1)..], headers);
        }
        catch (UriFormatException)
        {
            return Response(400, "{\"error\":\"bad request\"}");
        }
        if (!TokenMatches(token, given))
            return Response(401, "{\"error\":\"unauthorized\"}", "WWW-Authenticate: Bearer\r\n");
        return Response(200, snapshot, head: method == "HEAD");
    }

    private void AcceptLoop()
    {
        while (!stopped)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            if (!TryAdmit(client))
                continue;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Serve(client);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });
        }
    }

    private bool TryAdmit(TcpClient client)
    {
        if (Interlocked.Increment(ref active) <= MaxClients)
            return true;
        Interlocked.Decrement(ref active);
        client.Close();
        return false;
    }

    private void Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = UnauthTimeoutMs;
                client.SendTimeout = ClientTimeoutMs;
                NetworkStream stream = client.GetStream();
                byte[] reply = Encoding.UTF8.GetBytes(Respond(stream));
                stream.Write(reply, 0, reply.Length);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // A browser that navigated away or timed out needs no answer. The snapshot stays current for the next
                // request, which arrives on a connection of its own.
            }
        }
    }

    /// <summary>Read one newline-terminated line of at most <paramref name="max"/> bytes; null when the peer stops
    /// early or sends more.</summary>
    private static string? ReadLine(Stream stream, int max)
    {
        List<byte> bytes = [];
        int b;
        while ((b = stream.ReadByte()) >= 0)
        {
            if (b == '\n')
                return Encoding.UTF8.GetString([.. bytes]).TrimEnd('\r');
            if (bytes.Count >= max)
                return null;
            bytes.Add((byte)b);
        }
        return null;
    }

    private static bool TokenMatches(string expected, string? provided)
    {
        if (provided == null)
            return false;
        byte[] left = Encoding.UTF8.GetBytes(expected);
        byte[] right = Encoding.UTF8.GetBytes(provided);
        // Unity's Mono throws when the spans differ in length; net10 returns false.
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string? TokenOf(string query, IReadOnlyDictionary<string, string> headers)
    {
        if (headers.TryGetValue("Authorization", out string? auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();
        foreach (string pair in query.Split(['&'], StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && UnescapeQuery(pair[..eq]) == "token")
                return UnescapeQuery(pair[(eq + 1)..].Replace('+', ' '));
        }
        return null;
    }

    // Unity's Mono throws UriFormatException on a lone '%'; net10 UnescapeDataString does not.
    private static string UnescapeQuery(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '%')
                continue;
            if (i + 2 >= s.Length || !Uri.IsHexDigit(s[i + 1]) || !Uri.IsHexDigit(s[i + 2]))
                throw new UriFormatException();
            i += 2;
        }
        return Uri.UnescapeDataString(s);
    }

    private static string Response(int status, string body, string extraHeaders = "", bool head = false)
    {
        string reason = status switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", 405 => "Method Not Allowed", 431 => "Request Header Fields Too Large", _ => "Error" };
        int length = status == 204 ? 0 : Encoding.UTF8.GetByteCount(body);
        string payload = head || status == 204 ? "" : body;
        // Allow-Origin: the page OBS opens is a file:// URL, which fetches this loopback address cross-origin.
        return $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {length}\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: *\r\n{extraHeaders}Connection: close\r\n\r\n{payload}";
    }
}
