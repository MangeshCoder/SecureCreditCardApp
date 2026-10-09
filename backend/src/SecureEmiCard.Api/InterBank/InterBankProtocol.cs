namespace SecureEmiCard.Api.InterBank
{
    /// <summary>
    /// The wire protocol shared with partner banks. Partners implement exactly these rules
    /// (see tools/PartnerBankSimulator for a client implementation).
    ///
    /// REQUEST  POST /api/gateway/v1/authorize
    ///   X-Partner-Id : partner identifier
    ///   X-Timestamp  : Unix time in seconds (UTC)
    ///   X-Nonce      : random, single-use value (16-64 chars: letters, digits, '-', '_')
    ///   X-Signature  : Base64( HMAC-SHA256( signingKey, RequestCanonical(...) ) )
    ///   Body         : { "payload": "&lt;Base64( IV | AES-256-CBC(encryptionKey, JSON) )&gt;" }
    ///
    /// RESPONSE (from us)
    ///   X-Timestamp, X-Nonce (our own), X-Signature = Base64( HMAC-SHA256( signingKey, ResponseCanonical(...) ) )
    ///   Body         : { "payload": "&lt;encrypted JSON result&gt;" }
    ///
    /// The canonical strings put every security-relevant value into the signature, separated by '\n':
    /// changing the method, path, time, nonce or a single byte of the body invalidates the signature.
    /// The response string starts with "RESPONSE" and includes the REQUEST nonce, so a response can neither
    /// be replayed as a request nor be attached to a different request.
    /// </summary>
    public static class InterBankProtocol
    {
        public const string PartnerIdHeader = "X-Partner-Id";
        public const string TimestampHeader = "X-Timestamp";
        public const string NonceHeader = "X-Nonce";
        public const string SignatureHeader = "X-Signature";

        public static string RequestCanonical(string method, string path, string timestamp, string nonce, string body) =>
            string.Join('\n', method.ToUpperInvariant(), path, timestamp, nonce, body);

        public static string ResponseCanonical(int statusCode, string timestamp, string nonce, string requestNonce, string body) =>
            string.Join('\n', "RESPONSE", statusCode.ToString(), timestamp, nonce, requestNonce, body);

        public static bool IsValidNonce(string nonce) =>
            nonce.Length is >= 16 and <= 64 && nonce.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    /// <summary>JSON body of every gateway request and response.</summary>
    public sealed record EncryptedEnvelope(string Payload);
}
