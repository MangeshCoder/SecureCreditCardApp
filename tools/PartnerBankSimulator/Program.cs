// =====================================================================================
// Partner Bank Simulator - plays the role of an acquiring bank calling our gateway.
//
// It deliberately does NOT reference any SecureEmiCard project: it implements the protocol
// from the outside, exactly like a real partner bank would (see InterBankProtocol.cs).
//
// Usage (from the repository root):
//   dotnet run --project tools/PartnerBankSimulator -- --card 4581230000000000 --expiry 10/31 --cvv 123 --pin 2580 --amount 1500
// Options:
//   --merchant "Amazon India"   --mcc 5732
//   --channel Pos | Online | Contactless | Atm   --country IN      (Module 6: card controls)
//   --attack tamper | replay | stale | wrong-key     (shows how each attack is rejected)
// =====================================================================================
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var settings = JsonSerializer.Deserialize<PartnerSettings>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "partner-settings.json")),
    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
var opts = ParseArgs(args);

string Arg(string name, string? fallback = null) =>
    opts.TryGetValue(name, out var v) ? v : fallback ?? throw new ArgumentException($"Missing --{name}");

var expiry = Arg("expiry").Split('/');                       // MM/YY
var swipe = new
{
    cardNumber = Arg("card").Replace(" ", ""),
    expiryMonth = int.Parse(expiry[0]),
    expiryYear = 2000 + int.Parse(expiry[1]),
    cvv = Arg("cvv"),
    pin = Arg("pin"),
    merchantName = Arg("merchant", "Partner Bank Merchant"),
    merchantCategoryCode = Arg("mcc", "5999"),
    amount = decimal.Parse(Arg("amount")),
    channel = Arg("channel", "Pos"),
    merchantCountry = Arg("country", "IN")
};
var attack = Arg("attack", "none");

var encryptionKey = Convert.FromBase64String(settings.EncryptionKey);
var signingKey = Convert.FromBase64String(settings.SigningKey);
const string Path_ = "/api/gateway/v1/authorize";

// 1. Encrypt the card data: Base64( IV | AES-256-CBC(JSON) )
var plainJson = JsonSerializer.Serialize(swipe);
var body = JsonSerializer.Serialize(new { payload = Encrypt(plainJson, encryptionKey) });

// 2. Sign method, path, timestamp, nonce and body
var timestamp = DateTimeOffset.UtcNow.AddSeconds(attack == "stale" ? -600 : 0).ToUnixTimeSeconds().ToString();
var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
var canonical = string.Join('\n', "POST", Path_, timestamp, nonce, body);
var keyForSigning = attack == "wrong-key" ? RandomNumberGenerator.GetBytes(32) : signingKey;
var signature = Convert.ToBase64String(HMACSHA256.HashData(keyForSigning, Encoding.UTF8.GetBytes(canonical)));

if (attack == "tamper")
{
    // A "man in the middle" changes one character of the encrypted payload AFTER it was signed.
    var i = body.IndexOf("\"payload\":\"", StringComparison.Ordinal) + 15;
    body = body[..i] + (body[i] == 'A' ? 'B' : 'A') + body[(i + 1)..];
}

Console.WriteLine($"Partner      : {settings.PartnerId}");
Console.WriteLine($"Attack       : {attack}");
Console.WriteLine($"Plain request: {Mask(plainJson)}");
Console.WriteLine($"Wire body    : {body[..Math.Min(body.Length, 90)]}...");
Console.WriteLine();

using var http = new HttpClient { BaseAddress = new Uri(settings.ApiBaseUrl) };
await SendAsync();
if (attack == "replay")
{
    Console.WriteLine("\n--- Sending the EXACT same request again (replay) ---\n");
    await SendAsync();
}

async Task SendAsync()
{
    using var request = new HttpRequestMessage(HttpMethod.Post, Path_)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
    request.Headers.Add("X-Partner-Id", settings.PartnerId);
    request.Headers.Add("X-Timestamp", timestamp);
    request.Headers.Add("X-Nonce", nonce);
    request.Headers.Add("X-Signature", signature);

    using var response = await http.SendAsync(request);
    var responseBody = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"HTTP {(int)response.StatusCode} {response.StatusCode}");

    if (!response.Headers.TryGetValues("X-Signature", out var sigValues))
    {
        Console.WriteLine($"Unsigned response (request rejected before the secure channel): {responseBody}");
        return;
    }

    // 3. Verify the BANK's signature before trusting or decrypting anything
    var responseCanonical = string.Join('\n', "RESPONSE", ((int)response.StatusCode).ToString(),
        Header(response.Headers, "X-Timestamp"), Header(response.Headers, "X-Nonce"), nonce, responseBody);
    var expected = HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(responseCanonical));
    var provided = Convert.FromBase64String(sigValues.First());
    if (!CryptographicOperations.FixedTimeEquals(expected, provided))
    {
        Console.WriteLine("RESPONSE SIGNATURE INVALID - response discarded.");
        return;
    }
    Console.WriteLine("Response signature: valid");

    // 4. Decrypt the result
    var envelope = JsonDocument.Parse(responseBody).RootElement.GetProperty("payload").GetString()!;
    var result = Decrypt(envelope, encryptionKey);
    Console.WriteLine("Decrypted result  :");
    Console.WriteLine(JsonSerializer.Serialize(JsonDocument.Parse(result), new JsonSerializerOptions { WriteIndented = true }));
}

static string Encrypt(string plain, byte[] key)
{
    using var aes = Aes.Create();
    aes.Key = key;
    var iv = RandomNumberGenerator.GetBytes(16);
    var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(plain), iv, PaddingMode.PKCS7);
    return Convert.ToBase64String(iv.Concat(cipher).ToArray());
}

static string Decrypt(string base64, byte[] key)
{
    var data = Convert.FromBase64String(base64);
    using var aes = Aes.Create();
    aes.Key = key;
    return Encoding.UTF8.GetString(aes.DecryptCbc(data.AsSpan(16), data.AsSpan(0, 16), PaddingMode.PKCS7));
}

static string Header(HttpResponseHeaders headers, string name) =>
    headers.TryGetValues(name, out var v) ? v.First() : string.Empty;

static string Mask(string json) =>
    System.Text.RegularExpressions.Regex.Replace(json,
        "\"(cardNumber|cvv|pin)\":\"([^\"]*)\"", m => $"\"{m.Groups[1].Value}\":\"***\"");

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i].StartsWith("--")) result[args[i][2..]] = args[++i];
    return result;
}

record PartnerSettings(string ApiBaseUrl, string PartnerId, string EncryptionKey, string SigningKey);