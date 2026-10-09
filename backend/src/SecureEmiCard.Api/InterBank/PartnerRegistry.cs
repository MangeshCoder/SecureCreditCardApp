using Microsoft.Extensions.Options;

namespace SecureEmiCard.Api.InterBank
{
    /// <summary>A partner with its keys already decoded to bytes.</summary>
    public sealed record PartnerKeys(string PartnerId, string Name, byte[] EncryptionKey, byte[] SigningKey, bool Enabled);

    /// <summary>Looks up partner banks by X-Partner-Id. Keys are decoded once at start-up.</summary>
    public class PartnerRegistry
    {
        private readonly Dictionary<string, PartnerKeys> _partners;

        public PartnerRegistry(IOptions<InterBankOptions> options)
        {
            _partners = options.Value.Partners.ToDictionary(
                p => p.PartnerId,
                p => new PartnerKeys(p.PartnerId, p.Name, Convert.FromBase64String(p.EncryptionKey),
                                     Convert.FromBase64String(p.SigningKey), p.Enabled),
                StringComparer.Ordinal);
        }

        /// <summary>Returns the partner only if it exists AND is enabled.</summary>
        public PartnerKeys? FindActive(string partnerId) =>
            _partners.TryGetValue(partnerId, out var p) && p.Enabled ? p : null;
    }
}
