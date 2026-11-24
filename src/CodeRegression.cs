using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
namespace Kopra;

static class CodeRegression
{
    public static int Run()
    {
        int count = 0;
        void Check(bool condition)
        {
            count++;
            if (!condition)
                throw new Exception("Numeric pairing regression");
        }
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var server = new CodePairing(() => now);
        var session = Guid.NewGuid().ToString("N");
        var device = Guid.NewGuid().ToString("N");
        server.Begin(session, "synthetic-link", now + 300000);
        Check(server.Display.Length == 6 && server.Available);
        byte[] Send(object value)
        {
            using var request = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value));
            return server.Handle(request.RootElement);
        }
        bool Reject(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (IOException) { return true; }
        }
        Check(Reject(() => Send(new { kind = "code-start", session, device, pub = PairingCrypto.Base64(new byte[256]) })));
        var a = CodeCrypto.Random();
        var publicA = BigInteger.ModPow(2, a, CodeCrypto.N);
        using var challenge = JsonDocument.Parse(Send(new
        {
            kind = "code-start",
            session,
            device,
            pub = PairingCrypto.Base64(CodeCrypto.Pad(publicA))
        }));
        var fields = challenge.RootElement;
        var salt = PairingCrypto.Decode(fields.GetProperty("salt").GetString()!);
        var b = CodeCrypto.Number(PairingCrypto.Decode(fields.GetProperty("pub").GetString()!));
        string attempt = fields.GetProperty("attempt").GetString()!;
        var x = CodeCrypto.Password(server.Display, salt);
        var u = CodeCrypto.Scramble(publicA, b);
        var basis = ((b - CodeCrypto.K * BigInteger.ModPow(2, x, CodeCrypto.N)) % CodeCrypto.N + CodeCrypto.N) % CodeCrypto.N;
        var secret = BigInteger.ModPow(basis, a + u * x, CodeCrypto.N);
        var context = CodeCrypto.Context(session, device, salt, publicA, b);
        var key = CodeCrypto.Key(secret, salt, context);
        var fixture = new
        {
            code = server.Display,
            session,
            device,
            a = Convert.ToBase64String(a.ToByteArray(true, true)),
            b = Convert.ToBase64String(CodeCrypto.Pad(b)),
            salt = Convert.ToBase64String(salt),
            key = Convert.ToBase64String(key),
            context = Convert.ToBase64String(context),
            proof = Convert.ToBase64String(CodeCrypto.Proof(key, context, "client"))
        };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "code-fixture.json"), JsonSerializer.Serialize(fixture));
        using var ready = JsonDocument.Parse(Send(new
        {
            kind = "code-finish",
            session,
            attempt,
            proof = PairingCrypto.Base64(CodeCrypto.Proof(key, context, "client"))
        }));
        Check(ready.RootElement.GetProperty("kind").GetString() == "code-ready" && !server.Available);
        Check(System.Text.Encoding.UTF8.GetString(SyncWire.Open(PairingCrypto.Decode(ready.RootElement.GetProperty("cipher").GetString()!), key)) == "synthetic-link");
        Check(CryptographicOperations.FixedTimeEquals(PairingCrypto.Decode(ready.RootElement.GetProperty("proof").GetString()!), CodeCrypto.Proof(key, context, "server")));
        Check(Reject(() => Send(new { kind = "code-finish", session, attempt, proof = PairingCrypto.Base64(CodeCrypto.Proof(key, context, "client")) })));
        server.Begin(session, "synthetic-link", now + 300000);
        for (int i = 0; i < 5; i++)
        {
            using var pending = JsonDocument.Parse(Send(new
            {
                kind = "code-start",
                session,
                device,
                pub = PairingCrypto.Base64(CodeCrypto.Pad(publicA))
            }));
            string id = pending.RootElement.GetProperty("attempt").GetString()!;
            Check(Reject(() => Send(new { kind = "code-finish", session, attempt = id, proof = PairingCrypto.Base64(new byte[32]) })));
        }
        Check(!server.Available && Reject(() => Send(new { kind = "code-start", session, device, pub = PairingCrypto.Base64(CodeCrypto.Pad(publicA)) })));
        server.Begin(session, "synthetic-link", now + 300000);
        now += 300001;
        Check(!server.Available && Reject(() => Send(new { kind = "code-start", session, device, pub = PairingCrypto.Base64(CodeCrypto.Pad(publicA)) })));
        server.Dispose();
        Check(!server.Available && server.Display == "");
        return count;
    }
}
