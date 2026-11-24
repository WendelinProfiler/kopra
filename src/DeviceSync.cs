using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Kopra;

sealed record SharedFile(string Path, bool Directory, string Data);
sealed record SharedClip(string Id, string Kind, string Title, string Text, long Created, List<SharedFile> Files);
sealed record SyncPacket(int Version, string Device, string Id, long Issued, string Kind, string Ack, SharedClip? Entry);
sealed class SyncConfiguration
{
    public string Device { get; set; } = Guid.NewGuid().ToString("N");
    public string ProtectedKey { get; set; } = "";
    public string Peer { get; set; } = "";
    public long Since { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public List<string> Sent { get; set; } = [];
    public List<string> Received { get; set; } = [];
}
static class SyncWire
{
    public const int MaxContent = 32 * 1048576, MaxFrame = 48 * 1048576, Port = 53328;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly byte[] aad = Encoding.UTF8.GetBytes("Kopra/1");
    public static byte[] Seal(byte[] plain, byte[] key)
    {
        if (plain.Length > MaxFrame - 28)
            throw new IOException("Übertragung zu groß");
        byte[] result = new byte[plain.Length + 28];
        RandomNumberGenerator.Fill(result.AsSpan(0, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(result.AsSpan(0, 12), plain, result.AsSpan(12, plain.Length), result.AsSpan(12 + plain.Length, 16), aad);
        return result;
    }
    public static byte[] Open(byte[] sealedBox, byte[] key)
    {
        if (sealedBox.Length < 28 || sealedBox.Length > MaxFrame)
            throw new IOException("Ungültige Nachricht");
        byte[] plain = new byte[sealedBox.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(sealedBox.AsSpan(0, 12), sealedBox.AsSpan(12, plain.Length), sealedBox.AsSpan(12 + plain.Length, 16), plain, aad);
        return plain;
    }
    public static async Task<byte[]> Read(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 28 || length > MaxFrame)
            throw new IOException("Übertragung zu groß");
        byte[] data = new byte[length];
        await stream.ReadExactlyAsync(data, token);
        return data;
    }
    public static async Task Write(Stream stream, byte[] data, CancellationToken token)
    {
        byte[] size = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(size, data.Length);
        await stream.WriteAsync(size, token);
        await stream.WriteAsync(data, token);
    }
    public static string EntryId(ClipEntry entry) => entry.Id + "-" + new DateTimeOffset(entry.Created).ToUnixTimeMilliseconds();
    public static void Check(SharedClip clip)
    {
        if (clip.Id.Length is < 1 or > 100 || clip.Title.Length > 500 || clip.Text.Length > 1048576 || clip.Files.Count > 2000 || clip.Kind is not ("text" or "image" or "files"))
            throw new IOException("Ungültiger Eintrag");
        long size = Encoding.UTF8.GetByteCount(clip.Text);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in clip.Files)
        {
            var parts = f.Path.Split('/');
            if (f.Path.Length > 500 || parts.Length > 32 || parts.Any(x => string.IsNullOrWhiteSpace(x) || x is "." or ".." || x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || x.EndsWith('.') || x.EndsWith(' ') || new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "LPT1", "LPT2" }.Contains(x.Split('.')[0].ToUpperInvariant())) || !names.Add(f.Path))
                throw new IOException("Ungültiger Dateipfad");
            if (f.Directory && f.Data.Length > 0)
                throw new IOException("Ungültiger Ordner");
            size = checked(size + (long)f.Data.Length * 3 / 4);
            if (size > MaxContent)
                throw new IOException("Eintrag ist größer als 32 MB");
        }
        if (clip.Kind == "text" && clip.Files.Count != 0 || clip.Kind == "image" && (clip.Files.Count != 1 || clip.Files[0].Directory) || clip.Kind == "files" && clip.Files.Count == 0)
            throw new IOException("Unvollständiger Eintrag");
        foreach (var f in clip.Files)
        {
            var prefix = f.Path + "/";
            if (!f.Directory && clip.Files.Any(x => x.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Datei kann kein Ordner sein");
        }
    }
    public static SharedClip Pack(ClipEntry item)
    {
        if (item.Bytes > MaxContent)
            throw new IOException("Geräteübergreifend werden höchstens 32 MB pro Kopie übertragen.");
        var files = new List<SharedFile>();
        long size = 0;
        void Add(string path, string relative)
        {
            if (files.Count >= 2000)
                throw new IOException("Zu viele Dateien");
            var attr = File.GetAttributes(path);
            FileSafety.Check(path, attr);
            bool dir = Directory.Exists(path);
            byte[] bytes = dir ? [] : File.ReadAllBytes(path);
            size += bytes.Length;
            if (size > MaxContent)
                throw new IOException("Eintrag zu groß");
            files.Add(new(relative, dir, Convert.ToBase64String(bytes)));
            if (dir)
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
                Add(child, relative + "/" + Path.GetFileName(child));
        }
        if (item.Kind != "text")
        foreach (var file in item.Files)
            Add(file, Path.GetFileName(Path.TrimEndingDirectorySeparator(file)));
        var clip = new SharedClip(EntryId(item), item.Kind, item.Title, item.Text, new DateTimeOffset(item.Created).ToUnixTimeMilliseconds(), files);
        Check(clip);
        return clip;
    }
}
sealed class DeviceSync : IDisposable
{
    internal static void ExportTestPairing(string root, string destination)
    {
        if (!Path.GetFileName(root).StartsWith("kopra-lan-test-", StringComparison.Ordinal))
            throw new IOException("Testverzeichnis erforderlich");
        string file = Path.Combine(root, "devices.json");
        SyncConfiguration state;
        try
        {
            state = JsonSerializer.Deserialize<SyncConfiguration>(File.ReadAllText(file)) ?? new();
        }
        catch { state = new(); }
        byte[] secret;
        if (state.ProtectedKey.Length == 0)
        {
            secret = RandomNumberGenerator.GetBytes(32);
            state.ProtectedKey = Convert.ToBase64String(ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser));
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(state));
            File.Move(file + ".tmp", file, true);
        }
        else
            secret = ProtectedData.Unprotect(Convert.FromBase64String(state.ProtectedKey), null, DataProtectionScope.CurrentUser);
        var address = Dns.GetHostAddresses(Dns.GetHostName()).Where(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x)).FirstOrDefault() ?? IPAddress.Loopback;
        File.WriteAllText(destination, "IS1." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
        {
            host = address.ToString(),
            port = SyncWire.Port,
            device = state.Device,
            key = Convert.ToBase64String(secret)
        }, SyncWire.Json)));
    }
    readonly HistoryStore store; readonly ClipboardService clipboard; readonly Preferences prefs; readonly string file; readonly object gate = new(); readonly CancellationTokenSource stop = new();
    SyncConfiguration config; byte[]? key; TcpListener? listener; UdpClient? discovery; readonly HashSet<string> recentRequests = []; readonly HashSet<string> skipped = [];
    public string Status { get; private set; } = "Nur auf diesem PC";
    public DeviceSync(string root, HistoryStore store, ClipboardService clipboard, Preferences prefs)
    {
        this.store = store;
        this.clipboard = clipboard;
        this.prefs = prefs;
        file = Path.Combine(root, "devices.json");
        try
        {
            config = JsonSerializer.Deserialize<SyncConfiguration>(File.ReadAllText(file)) ?? new();
            if (config.ProtectedKey.Length > 0)
                key = ProtectedData.Unprotect(Convert.FromBase64String(config.ProtectedKey), null, DataProtectionScope.CurrentUser);
        }
        catch { config = new(); }
        Pairing = new PairingServer(config.Device, InstallKey);
        if (key != null)
            Start();
    }
    void Save()
    {
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(config));
        File.Move(file + ".tmp", file, true);
    }
    public PairingServer Pairing { get; private set; } = null!;
    public bool HasPeer
    {
        get
        {
            lock (gate)
                return key != null && config.Peer.Length > 0;
        }
    }
    public string BeginPairing()
    {
        if (HasPeer)
            throw new IOException("Bitte zuerst die bestehende Verbindung aufheben.");
        Start();
        if (listener == null)
            throw new IOException("Die Geräteverbindung konnte nicht gestartet werden.");
        var addresses = Dns.GetHostAddresses(Dns.GetHostName()).Where(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x) && x.GetAddressBytes()[0] != 169).ToArray();
        if (addresses.Length == 0)
            throw new IOException("Kein lokales Netzwerk gefunden.");
        return Pairing.Begin(addresses[0].ToString());
    }
    void InstallKey(byte[] secret, string peer)
    {
        lock (gate)
        {
            key = secret;
            config.ProtectedKey = Convert.ToBase64String(ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser));
            config.Peer = peer;
            config.Since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            config.Sent.Clear();
            config.Received.Clear();
            recentRequests.Clear();
            skipped.Clear();
            Save();
            Status = "Mit Mac verbunden";
            StartDiscovery();
        }
    }
    public void CancelPairing()
    {
        Pairing.Cancel();
        lock (gate)
        {
            if (key == null)
            {
                listener?.Stop();
                listener = null;
                discovery?.Dispose();
                discovery = null;
                Status = "Nur auf diesem PC";
            }
        }
    }
    void StartDiscovery()
    {
        if (key == null || discovery != null)
            return;
        try
        {
            discovery = new UdpClient(SyncWire.Port + 1);
            _ = Discover(discovery);
        }
        catch (SocketException) { Status = "Gerätesuche eingeschränkt. Die direkte Verbindung bleibt verfügbar."; }
    }
    public void Unpair()
    {
        Pairing.Reset();
        lock (gate)
        {
            if (key != null)
                CryptographicOperations.ZeroMemory(key);
            key = null;
            listener?.Stop();
            listener = null;
            discovery?.Dispose();
            discovery = null;
            config = new()
            {
                Device = config.Device
            };
            Save();
            Status = "Nur auf diesem PC";
        }
    }
    void Start()
    {
        if (listener != null)
            return;
        try
        {
            listener = new TcpListener(IPAddress.Any, SyncWire.Port);
            listener.Start(4);
            Status = "Wartet auf Mac";
            _ = Listen(listener);
            StartDiscovery();
        }
        catch (SocketException) { listener?.Stop(); listener = null; Status = "Geräteverbindung konnte nicht gestartet werden"; }
    }
    async Task Discover(UdpClient socket)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var datagram = await socket.ReceiveAsync(stop.Token);
                byte[] secret;
                lock (gate)
                {
                    if (key == null)
                        continue;
                    secret = key.ToArray();
                }
                if (datagram.Buffer.Length > 2048)
                    continue;
                var request = JsonSerializer.Deserialize<SyncPacket>(SyncWire.Open(datagram.Buffer, secret), SyncWire.Json);
                if (request == null || request.Kind != "discover" || request.Version != 1 || !Guid.TryParseExact(request.Id, "N", out _) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - request.Issued) > 60000)
                    continue;
                lock (gate)
                {
                    if (config.Peer.Length > 0 && config.Peer != request.Device)
                        continue;
                }
                var response = new SyncPacket(1, config.Device, request.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "discovered", "", null);
                await socket.SendAsync(SyncWire.Seal(JsonSerializer.SerializeToUtf8Bytes(response, SyncWire.Json), secret), datagram.RemoteEndPoint, stop.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { await Task.Delay(100, stop.Token).ConfigureAwait(false); }
        }
    }
    async Task Listen(TcpListener server)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var client = await server.AcceptTcpClientAsync(stop.Token);
                _ = Handle(client);
            }
            catch { break; }
        }
    }
    readonly SemaphoreSlim connections = new(1, 1);
    async Task Handle(TcpClient client)
    {
        using (client)
        {
            if (!await connections.WaitAsync(0))
                return;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                var token = timeout.Token;
                var network = client.GetStream();
                var frame = await SyncWire.Read(network, token);
                if (Pairing.TryHandle(frame, out var pairingReply))
                {
                    await SyncWire.Write(network, pairingReply, token);
                    return;
                }
                byte[] secret;
                lock (gate)
                {
                    if (key == null)
                        return;
                    secret = key.ToArray();
                }
                var packet = JsonSerializer.Deserialize<SyncPacket>(SyncWire.Open(frame, secret), SyncWire.Json) ?? throw new IOException();
                if (packet.Version != 1 || packet.Kind != "exchange" || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - packet.Issued) > 60000 || packet.Ack.Length > 100 || !Guid.TryParseExact(packet.Id, "N", out _) || !Guid.TryParseExact(packet.Device, "N", out _) || packet.Device == config.Device)
                    throw new IOException("Ungültige Geräteanfrage");
                lock (gate)
                {
                    if (key == null || !CryptographicOperations.FixedTimeEquals(key, secret))
                        return;
                    if (config.Peer.Length > 0 && config.Peer != packet.Device)
                        throw new IOException("Unbekanntes Gerät");
                    if (!recentRequests.Add(packet.Id))
                        throw new IOException("Wiederholte Anfrage");
                    if (recentRequests.Count > 256)
                        recentRequests.Remove(recentRequests.First());
                    if (config.Peer.Length == 0)
                    {
                        config.Peer = packet.Device;
                        Save();
                    }
                    if (packet.Ack.Length > 0 && !config.Sent.Contains(packet.Ack))
                    {
                        config.Sent.Add(packet.Ack);
                        if (config.Sent.Count > 4096)
                            config.Sent.RemoveAt(0);
                        Save();
                    }
                }
                string ack = "";
                if (packet.Entry != null)
                {
                    bool seen;
                    lock (gate)
                        seen = config.Received.Contains(packet.Entry.Id);
                    if (!seen)
                    {
                        if (prefs.Paused)
                            throw new IOException("Verlauf pausiert");
                        await clipboard.ImportShared(packet.Entry, "Mac", packet.Device, token);
                        lock (gate)
                        {
                            config.Received.Add(packet.Entry.Id);
                            if (config.Received.Count > 4096)
                                config.Received.RemoveAt(0);
                            Save();
                        }
                    }
                    ack = packet.Entry.Id;
                }
                SharedClip? outgoing = null;
                foreach (var item in store.All().Where(x => x.OriginDevice.Length == 0 && new DateTimeOffset(x.Created).ToUnixTimeMilliseconds() >= config.Since).OrderBy(x => x.Created))
                {
                    var id = SyncWire.EntryId(item);
                    if (config.Sent.Contains(id) || skipped.Contains(id) || prefs.Paused)
                        continue;
                    try
                    {
                        outgoing = SyncWire.Pack(item);
                        break;
                    }
                    catch (IOException) { skipped.Add(id); Status = "Eine Kopie konnte nicht übertragen werden (max. 32 MB)."; }
                }
                var response = new SyncPacket(1, config.Device, packet.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "response", ack, outgoing);
                await SyncWire.Write(network, SyncWire.Seal(JsonSerializer.SerializeToUtf8Bytes(response, SyncWire.Json), secret), token);
                Status = "Mit Mac verbunden";
            }
            catch (OperationCanceledException) { Status = "Verbindung unterbrochen – versucht es erneut"; }
            catch (Exception) { Status = "Übertragung wartet – Verbindung oder Speicher prüfen"; }
            finally { connections.Release(); }
        }
    }
    public void Dispose()
    {
        stop.Cancel();
        listener?.Stop();
        discovery?.Dispose();
        Pairing.Dispose();
    }
}
