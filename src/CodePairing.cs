using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Kopra;

// SRP-6a arithmetic from RFC 5054, appendix A's fixed 2048-bit group.
// SHA-256, transcript-bound HKDF and explicit key confirmation are used here.
static class CodeCrypto
{
    public const string Prime = "AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050A37329CBB4A099ED8193E0757767A13DD52312AB4B03310DCD7F48A9DA04FD50E8083969EDB767B0CF6095179A163AB3661A05FBD5FAAAE82918A9962F0B93B855F97993EC975EEAA80D740ADBF4FF747359D041D5C33EA71D281E446B14773BCA97B43A23FB801676BD207A436C6481F1D2B9078717461A5B9D32E688F87748544523B524B0D57D5EA77A2775D2ECFA032CFBDBF52FB3786160279004E57AE6AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DBFBB694B5C803D89F7AE435DE236D525F54759B65E372FCD68EF20FA7111F9E4AFF73";
    public static readonly BigInteger N = Number(Convert.FromHexString(Prime));
    public static readonly BigInteger K = Number(SHA256.HashData([.. Pad(N), .. Pad(2)]));
    public static BigInteger Number(byte[] bytes) => new(bytes, isUnsigned: true, isBigEndian: true);
    public static byte[] Pad(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length > 256)
            throw new IOException("Ungültiger Schlüsselaustausch");
        return [.. new byte[256 - bytes.Length], .. bytes];
    }
    public static BigInteger Random() => Number(RandomNumberGenerator.GetBytes(32)) + 1;
    public static BigInteger Password(string code, byte[] salt) => Number(SHA256.HashData([.. salt, .. SHA256.HashData(Encoding.UTF8.GetBytes("Kopra:" + code))]));
    public static BigInteger Scramble(BigInteger a, BigInteger b)
    {
        var u = Number(SHA256.HashData([.. Pad(a), .. Pad(b)]));
        if (u.IsZero)
            throw new IOException();
        return u;
    }
    public static byte[] Context(string session, string device, byte[] salt, BigInteger a, BigInteger b) => SHA256.HashData([.. Encoding.UTF8.GetBytes("kopra-code-v1|" + session + "|" + device + "|"), .. salt, .. Pad(a), .. Pad(b)]);
    public static byte[] Key(BigInteger secret, byte[] salt, byte[] context) => HKDF.DeriveKey(HashAlgorithmName.SHA256, SHA256.HashData(Pad(secret)), 32, salt, context);
    public static byte[] Proof(byte[] key, byte[] context, string role) => HMACSHA256.HashData(key, (byte[])[.. context, .. Encoding.UTF8.GetBytes(role)]);
}

sealed class CodePairing : IDisposable
{
    readonly Dictionary<string, (byte[] Key, byte[] Context)> attempts = new();
    readonly Func<long> now;
    string code = "", session = "", link = ""; long expires; int tries; bool redeemed;
    public string Display => code;
    public bool Available => code.Length > 0 && now() < expires && tries < 5 && !redeemed;
    public string Session => session;
    public CodePairing(Func<long> now)
    {
        this.now = now;
    }
    public void Begin(string session, string link, long expires)
    {
        Dispose();
        this.session = session;
        this.link = link;
        this.expires = expires;
        tries = 0;
        redeemed = false;
        code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
    }
    public byte[] Handle(JsonElement request)
    {
        var kind = request.GetProperty("kind").GetString();
        if (code.Length == 0 || now() >= expires || redeemed || request.GetProperty("session").GetString() != session)
            throw new IOException();
        if (kind == "code-start")
        {
            if (tries++ >= 5)
                throw new IOException();
            string device = request.GetProperty("device").GetString() ?? "";
            if (!Guid.TryParseExact(device, "N", out _))
                throw new IOException();
            var a = CodeCrypto.Number(PairingCrypto.Decode(request.GetProperty("pub").GetString() ?? "", 350));
            if (a <= 0 || a >= CodeCrypto.N)
                throw new IOException();
            var salt = RandomNumberGenerator.GetBytes(32);
            var b = CodeCrypto.Random();
            var v = BigInteger.ModPow(2, CodeCrypto.Password(code, salt), CodeCrypto.N);
            var publicB = (CodeCrypto.K * v + BigInteger.ModPow(2, b, CodeCrypto.N)) % CodeCrypto.N;
            var secret = BigInteger.ModPow(a * BigInteger.ModPow(v, CodeCrypto.Scramble(a, publicB), CodeCrypto.N) % CodeCrypto.N, b, CodeCrypto.N);
            var context = CodeCrypto.Context(session, device, salt, a, publicB);
            string id = Guid.NewGuid().ToString("N");
            attempts[id] = (CodeCrypto.Key(secret, salt, context), context);
            return JsonSerializer.SerializeToUtf8Bytes(new
            {
                kind = "code-challenge",
                attempt = id,
                session,
                salt = PairingCrypto.Base64(salt),
                pub = PairingCrypto.Base64(CodeCrypto.Pad(publicB))
            }, SyncWire.Json);
        }
        if (kind != "code-finish" || !attempts.Remove(request.GetProperty("attempt").GetString() ?? "", out var pending))
            throw new IOException();
        try
        {
            var proof = PairingCrypto.Decode(request.GetProperty("proof").GetString() ?? "", 64);
            if (!CryptographicOperations.FixedTimeEquals(proof, CodeCrypto.Proof(pending.Key, pending.Context, "client")))
                throw new IOException();
            redeemed = true;
            return JsonSerializer.SerializeToUtf8Bytes(new
            {
                kind = "code-ready",
                session,
                proof = PairingCrypto.Base64(CodeCrypto.Proof(pending.Key, pending.Context, "server")),
                cipher = PairingCrypto.Base64(SyncWire.Seal(Encoding.UTF8.GetBytes(link), pending.Key))
            }, SyncWire.Json);
        }
        finally { CryptographicOperations.ZeroMemory(pending.Key); }
    }
    public void Dispose()
    {
        foreach (var pending in attempts.Values)
            CryptographicOperations.ZeroMemory(pending.Key);
        attempts.Clear();
        code = "";
        link = "";
        session = "";
        redeemed = true;
    }
}
