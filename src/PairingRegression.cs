using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Kopra;

static class PairingRegression
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string message)
        {
            count++;
            if (!value)
                throw new Exception("Pairing: " + message);
        }
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string host = Guid.NewGuid().ToString("N"), client = Guid.NewGuid().ToString("N");
        byte[]? installed = null;
        string installedPeer = "";
        int commits = 0;
        using var server = new PairingServer(host, (key, peer) => { installed = key; installedPeer = peer; commits++; }, () => now);
        string qr = server.Begin("127.0.0.1");
        var fields = new Uri(qr).Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
        Check(fields["v"] == "2" && !fields.ContainsKey("key"), "QR contains no permanent key");
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        string pub = PairingCrypto.Base64(PairingCrypto.Public(ephemeral));
        byte[] token = PairingCrypto.Decode(fields["token"]), shared = PairingCrypto.Secret(ephemeral, PairingCrypto.Decode(fields["pub"]));
        string session = fields["session"];
        byte[] handshake = PairingCrypto.Derive(shared, token, session), sync = PairingCrypto.Derive(shared, token, session, true);
        byte[] Request(byte[] proof) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            kind = "pair-request",
            session,
            device = client,
            pub,
            proof = PairingCrypto.Base64(proof)
        }, SyncWire.Json);
        string State(byte[] response)
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.GetProperty("kind").GetString() == "pair-error")
                return "error";
            using var state = JsonDocument.Parse(SyncWire.Open(PairingCrypto.Decode(doc.RootElement.GetProperty("cipher").GetString()!), handshake));
            return state.RootElement.GetProperty("status").GetString()!;
        }
        byte[] Action(string action) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            kind = "pair-exchange",
            session,
            cipher = PairingCrypto.Base64(SyncWire.Seal(JsonSerializer.SerializeToUtf8Bytes(new
            {
                device = client,
                action
            }, SyncWire.Json), handshake))
        }, SyncWire.Json);
        server.TryHandle(Request(RandomNumberGenerator.GetBytes(32)), out var reply);
        Check(State(reply) == "error" && commits == 0, "wrong QR token rejected");
        var correct = Request(PairingCrypto.Proof(token, session, client, pub));
        server.TryHandle(correct, out reply);
        Check(State(reply) == "waiting", "scan waits for confirmation");
        Check(server.Snapshot.Code == PairingCrypto.Code(handshake), "matching six digit code");
        Check(commits == 0, "scan alone does not pair");
        server.Approve();
        Check(commits == 0, "host approval alone does not pair");
        server.TryHandle(Action("approve"), out reply);
        Check(State(reply) == "complete" && commits == 1, "both sides must approve");
        Check(installed!.SequenceEqual(sync) && installedPeer == client, "derived sync key and peer persisted");
        server.TryHandle(Action("approve"), out reply);
        Check(State(reply) == "complete" && commits == 1, "lost completion response can retry without another pairing");
        server.Reset();
        server.TryHandle(Action("poll"), out reply);
        Check(State(reply) == "error", "unpair invalidates completion receipt");
        qr = server.Begin("127.0.0.1");
        now += 300001;
        server.TryHandle(correct, out reply);
        Check(State(reply) == "error", "expired or replaced code rejected");
        Check(server.Snapshot.Status == "expired", "expiry visible");
        server.Begin("127.0.0.1");
        server.Cancel();
        Check(server.Snapshot.Status == "rejected", "cancellation visible");
        using var a = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var b = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var aShared = PairingCrypto.Secret(a, PairingCrypto.Public(b));
        var bShared = PairingCrypto.Secret(b, PairingCrypto.Public(a));
        Check(aShared.SequenceEqual(bShared), "P-256 shared secrets match");
        var fixture = new
        {
            privateKey = Convert.ToBase64String(a.ExportParameters(true).D!),
            peerPublic = Convert.ToBase64String(PairingCrypto.Public(b)),
            token = Convert.ToBase64String(token),
            session,
            expected = Convert.ToBase64String(PairingCrypto.Derive(aShared, token, session)),
            syncExpected = Convert.ToBase64String(PairingCrypto.Derive(aShared, token, session, true)),
            code = PairingCrypto.Code(PairingCrypto.Derive(aShared, token, session))
        };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "pair-v2-fixture.json"), JsonSerializer.Serialize(fixture));
        using (var generator = new QRCoder.QRCodeGenerator())
        using (var data = generator.CreateQrCode(qr, QRCoder.QRCodeGenerator.ECCLevel.Q))
        using (var png = new QRCoder.PngByteQRCode(data))
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "qr-test.png"), png.GetGraphic(8));
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "qr-test.txt"), qr);
        Check(Shortcuts.Valid(77, 3) && !Shortcuts.Valid(17, 0) && !Shortcuts.Valid(76, 8), "hotkey validation");
        Check(Shortcuts.Label(77, 3) == "Strg + Alt + M", "hotkey label");
        count += CodeRegression.Run();
        return count;
    }
}
