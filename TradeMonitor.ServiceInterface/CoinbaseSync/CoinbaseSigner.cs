using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace TradeMonitor.ServiceInterface;

/// <summary>
/// CDP JWT for GET requests. The private key stays in this object and is cleared on dispose.
/// </summary>
public sealed class CoinbaseSigner : IDisposable
{
    readonly string keyName;
    readonly string algorithm;
    readonly byte[]? ed25519Seed;
    readonly ECDsa? ecdsa;

    CoinbaseSigner(string keyName, string algorithm, byte[]? ed25519Seed, ECDsa? ecdsa)
    {
        this.keyName = keyName;
        this.algorithm = algorithm;
        this.ed25519Seed = ed25519Seed;
        this.ecdsa = ecdsa;
    }

    public string Algorithm => algorithm;

    public static CoinbaseSigner Load(string keyName, string privateKey)
    {
        var pk = privateKey.Trim().Replace("\\n", "\n", StringComparison.Ordinal);
        if (pk.Contains("BEGIN", StringComparison.Ordinal))
        {
            var ecdsa = ECDsa.Create();
            try
            {
                ecdsa.ImportFromPem(pk);
            }
            catch (Exception)
            {
                ecdsa.Dispose();
                throw new InvalidOperationException("Unrecognized Coinbase private key format.");
            }
            return new CoinbaseSigner(keyName, "ES256", null, ecdsa);
        }

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(PadBase64(pk));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Unrecognized Coinbase private key format.");
        }
        if (raw.Length is not (32 or 64))
        {
            CryptographicOperations.ZeroMemory(raw);
            throw new InvalidOperationException("Unrecognized Coinbase private key format.");
        }
        var seed = raw.AsSpan(0, 32).ToArray();
        CryptographicOperations.ZeroMemory(raw);
        return new CoinbaseSigner(keyName, "EdDSA", seed, null);
    }

    public string Create(string method, string path)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = HeaderJson(algorithm, keyName, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
        var payload = PayloadJson(keyName, now, method + " api.coinbase.com" + path);
        var signingInput = Base64Url(header) + "." + Base64Url(payload);
        var signature = Sign(Encoding.ASCII.GetBytes(signingInput));
        return signingInput + "." + Base64Url(signature);
    }

    byte[] Sign(byte[] input)
    {
        if (ed25519Seed != null)
        {
            var signer = new Ed25519Signer();
            signer.Init(true, new Ed25519PrivateKeyParameters(ed25519Seed, 0));
            signer.BlockUpdate(input, 0, input.Length);
            return signer.GenerateSignature();
        }
        return ecdsa!.SignData(input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public void Dispose()
    {
        if (ed25519Seed != null)
            CryptographicOperations.ZeroMemory(ed25519Seed);
        ecdsa?.Dispose();
    }

    static string HeaderJson(string alg, string kid, string nonce)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("alg", alg);
            writer.WriteString("kid", kid);
            writer.WriteString("nonce", nonce);
            writer.WriteString("typ", "JWT");
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static string PayloadJson(string keyName, long now, string uri)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("sub", keyName);
            writer.WriteString("iss", "cdp");
            writer.WriteNumber("nbf", now);
            writer.WriteNumber("exp", now + 120);
            writer.WriteString("uri", uri);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Base64Url(string text) => Base64Url(Encoding.UTF8.GetBytes(text));

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static string PadBase64(string value)
    {
        var pad = (4 - value.Length % 4) % 4;
        return pad == 0 ? value : value + new string('=', pad);
    }
}
