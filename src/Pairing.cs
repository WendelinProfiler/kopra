using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Kopra;

sealed record PairingSnapshot(string Status, string Code, long Expires, string Device = "", bool HostApproved = false, bool Numeric = false, bool PeerApproved = false);
static class PairingCrypto
{
    public static string Base64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Decode(string value, int maximum = 1024)
    {
        if (value.Length > maximum)
            throw new IOException(L.Text("Code zu lang"));
        string text = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '='));
    }
    public static byte[] Public(ECDiffieHellman key)
    {
        var p = key.ExportParameters(false);
        return [4, .. p.Q.X!, .. p.Q.Y!];
    }
    public static byte[] Secret(ECDiffieHellman key, byte[] publicKey)
    {
        if (publicKey.Length != 65 || publicKey[0] != 4)
            throw new IOException(L.Text("Ungültiger Geräteschlüssel"));
        using var peer = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] } });
        return key.DeriveRawSecretAgreement(peer.PublicKey);
    }
    public static byte[] Derive(byte[] shared, byte[] token, string session, bool sync = false) => HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, SHA256.HashData(token), Encoding.UTF8.GetBytes((sync ? "kopra-sync-v1:" : "kopra-pair-v2:") + session));
    public static byte[] Proof(byte[] token, string session, string device, string publicKey) => HMACSHA256.HashData(token, Encoding.UTF8.GetBytes("kopra-pair-v2|" + session + "|" + device + "|" + publicKey));
    public static string Code(byte[] key)
    {
        var hash = SHA256.HashData([.. key, .. Encoding.UTF8.GetBytes("kopra-confirm-v2")]);
        uint number = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash);
        return (number % 1000000).ToString("D6");
    }
}

sealed class PairingServer : IDisposable
{
    readonly object gate = new(); readonly string device; readonly Action<byte[], string> complete; ECDiffieHellman? ephemeral; byte[] token = [], handshake = [], sync = []; string session = "", peer = "", peerPublic = "", code = ""; long expires, receiptUntil; bool hostApproved, peerApproved, completed, rejected;
    readonly Func<long> now;
    readonly CodePairing numeric;
    public string EntryCode
    {
        get
        {
            lock (gate)
                return numeric.Display;
        }
    }
    public string? DiscoverySession
    {
        get
        {
            lock (gate)
                return !rejected && !completed && peer.Length == 0 && numeric.Available ? numeric.Session : null;
        }
    }
    public PairingServer(string device, Action<byte[], string> complete, Func<long>? now = null)
    {
        this.device = device;
        this.complete = complete;
        this.now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        numeric = new CodePairing(this.now);
    }
    public PairingSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                string status = completed ? "complete" : rejected ? "rejected" : expires <= now() ? "expired" : peer.Length == 0 ? "scanning" : "confirmation";
                return new(status, code, expires, peer.Length > 0 ? "Mac" : "", hostApproved, numeric.Authenticated, peerApproved);
            }
        }
    }
    public string Begin(string host)
    {
        lock (gate)
        {
            Clear();
            ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            token = RandomNumberGenerator.GetBytes(32);
            session = Guid.NewGuid().ToString("N");
            expires = now() + 300000;
            var link = "kopra://pair?v=2&host=" + Uri.EscapeDataString(host) + "&port=" + SyncWire.Port + "&device=" + device + "&session=" + session + "&pub=" + PairingCrypto.Base64(PairingCrypto.Public(ephemeral)) + "&token=" + PairingCrypto.Base64(token) + "&expires=" + expires;
            numeric.Begin(session, link, expires);
            return link;
        }
    }
    public void Approve()
    {
        lock (gate)
        {
            if (!completed && !rejected && expires > now() && peer.Length > 0)
                hostApproved = true;
        }
    }
    public void Cancel()
    {
        lock (gate)
        {
            if (!completed)
            {
                rejected = true;
                numeric.Dispose();
                CryptographicOperations.ZeroMemory(sync);
            }
        }
    }
    public void Reset()
    {
        lock (gate)
            Clear();
    }
    public bool TryHandle(byte[] input, out byte[] response)
    {
        response = [];
        if (input.Length > 16384 || input.Length == 0 || input[0] != '{')
            return false;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(input);
        }
        catch { return false; }
        using (document)
        {
            var json = document.RootElement;
            if (!json.TryGetProperty("kind", out var kindValue))
                return false;
            string? kind = kindValue.GetString();
            if (kind is not ("pair-request" or "pair-exchange" or "code-start" or "code-finish"))
                return false;
            lock (gate)
            {
                try
                {
                    if (ephemeral == null || !json.TryGetProperty("session", out var sid) || sid.GetString() != session || rejected || (!completed && expires <= now()) || (completed && receiptUntil <= now()))
                        throw new IOException();
                    if (kind is "code-start" or "code-finish")
                    {
                        response = numeric.Handle(json);
                        return true;
                    }
                    if (kind == "pair-request")
                    {
                        string candidate = json.GetProperty("device").GetString() ?? "", pub = json.GetProperty("pub").GetString() ?? "";
                        if (!Guid.TryParseExact(candidate, "N", out _) || candidate == device)
                            throw new IOException();
                        byte[] supplied = PairingCrypto.Decode(json.GetProperty("proof").GetString() ?? "", 128);
                        if (!CryptographicOperations.FixedTimeEquals(PairingCrypto.Proof(token, session, candidate, pub), supplied))
                            throw new CryptographicException();
                        if (peer.Length > 0 && (peer != candidate || peerPublic != pub))
                            throw new IOException();
                        if (peer.Length == 0)
                        {
                            var shared = PairingCrypto.Secret(ephemeral, PairingCrypto.Decode(pub, 256));
                            handshake = PairingCrypto.Derive(shared, token, session);
                            sync = PairingCrypto.Derive(shared, token, session, true);
                            CryptographicOperations.ZeroMemory(shared);
                            peer = candidate;
                            peerPublic = pub;
                            code = PairingCrypto.Code(handshake);
                        }
                    }
                    else
                    {
                        if (peer.Length == 0)
                            throw new IOException();
                        byte[] inner = SyncWire.Open(PairingCrypto.Decode(json.GetProperty("cipher").GetString() ?? "", 8192), handshake);
                        using var payload = JsonDocument.Parse(inner);
                        if (payload.RootElement.GetProperty("device").GetString() != peer)
                            throw new IOException();
                        string? action = payload.RootElement.GetProperty("action").GetString();
                        if (action == "approve")
                            peerApproved = true;
                        else if (action == "reject")
                        {
                            rejected = true;
                            numeric.Dispose();
                        }
                        else if (action != "poll")
                            throw new IOException();
                    }
                    if (hostApproved && peerApproved && !completed && !rejected)
                    {
                        complete(sync.ToArray(), peer);
                        completed = true;
                        receiptUntil = now() + 120000;
                        CryptographicOperations.ZeroMemory(sync);
                    }
                    var state = new
                    {
                        status = completed ? "complete" : rejected ? "rejected" : hostApproved ? "approved" : "waiting",
                        device,
                        session,
                        code,
                        expires
                    };
                    response = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        kind = "pair-reply",
                        session,
                        cipher = PairingCrypto.Base64(SyncWire.Seal(JsonSerializer.SerializeToUtf8Bytes(state, SyncWire.Json), handshake))
                    }, SyncWire.Json);
                    return true;
                }
                catch (Exception error) when (error is IOException or CryptographicException or FormatException or KeyNotFoundException or InvalidOperationException) { response = JsonSerializer.SerializeToUtf8Bytes(new { kind = "pair-error", status = "unavailable" }, SyncWire.Json); return true; }
            }
        }
    }
    void Clear()
    {
        numeric.Dispose();
        ephemeral?.Dispose();
        ephemeral = null;
        foreach (var secret in new[] { token, handshake, sync })
            CryptographicOperations.ZeroMemory(secret);
        token = [];
        handshake = [];
        sync = [];
        peer = "";
        peerPublic = "";
        code = "";
        completed = false;
        rejected = false;
        hostApproved = false;
        peerApproved = false;
        receiptUntil = 0;
        expires = 0;
        session = "";
    }
    public void Dispose()
    {
        lock (gate)
            Clear();
    }
}
