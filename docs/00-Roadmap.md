# Roadmap – building the Secure Credit EMI Card Service module by module

The specification describes the core capabilities (Modules 1–5). Modules 6–11 add the features that
EMI credit cards on the market offer, and Module 12 deploys everything. Each module is self-contained and
goes through every layer: SQL script → Domain → Application → Infrastructure → API → Angular → tests.
Each module ends with a working, tested application, so you always have something you can run.

| Module | What you build | Spec section | New tables |
|---|---|---|---|
| **1. Foundation + Cardholders & Cards** ✅ | Solution structure, JWT auth, onboarding, card issuance, credit limit, Active/Blocked status, CVV/PIN hashing, AES card-number encryption | §1 Cardholder & Lifecycle, CVV & PIN Security, §2, §4A | `Cardholders`, `CreditCards` |
| **2. Merchant Swipe & Load** ✅ | Swipe authorization (card status, expiry, CVV/PIN, available balance), HMAC blind index for card lookup, 3-strikes PIN lockout, balance loads/repayments, refunds (with credit balance), transaction ledger, optimistic-concurrency-safe balance updates | §1 Merchant Swipe & Load | `Transactions` |
| **3. Cashback Engine** ✅ | Rules by Merchant Category Code (5411 groceries 3 %, 5812 dining 3 %, 5541 fuel 2 %, others 1 %), cashback credited per swipe, cashback history | §1 Cashback, §4B | `CashbackLogs` |
| **4. EMI Engine** ✅ | EMI preview calculator, convert eligible transactions (amount > 100, not converted) to 3/6/12/24-month plans, amortization schedule, installment payments | §1 EMI Conversion, §4B, §4C, §5 | `EmiPlans`, `EmiSchedules` |
| **5. Inter-Bank Payload Security** ✅ | AES-256 encrypted payloads from partner banks, HMAC-SHA256 digital signatures verified in middleware, replay protection, security audit log | §1 Inter-Bank, §4A, §6 | `SecurityAuditLogs` |

### Modules 6–12: market features

| Module | What you build | Why |
|---|---|---|
| **6. Card Controls & Spending Limits** | Switch online, international, contactless and ATM use on/off per card; daily and per-transaction limits; temporary self-lock; enforced on every swipe path, including the gateway | Every bank's card app has it; RBI rules require these controls |
| **7. OTP / Two-Factor Authentication + Notifications** | One-time passwords for sensitive actions (PIN change, card number reveal, limit changes), in-app/e-mail alerts for every transaction | Stops account takeover with a stolen password; the customer sees every spend immediately |
| **8. Billing Cycle & Statements** | Monthly statement, minimum amount due, due date, late fee, interest on unpaid balance, PDF download | The core of how a credit card actually charges the customer |
| **9. EMI Extras** | Processing fee + GST on EMI conversion, foreclosure (close an EMI early), Key Fact Statement shown before conversion | Real EMI products disclose total cost up front and allow early closure |
| **10. Fraud Detection Rules** | Velocity checks, unusual amount / location rules, automatic hold + customer confirmation | Catches stolen-card spending before the limit is gone |
| **11. Rewards 2.0** | Reward points, milestone bonuses, redemption against the statement | Market cards combine cashback with points and milestones |
| **12. Deployment** | Dockerfile, Azure App Service + Static Web Apps, Azure SQL with Always Encrypted, Key Vault, shared nonce store (Redis), CI/CD pipeline | Spec §6: running it securely in the cloud |

## Where we deliberately improve on the specification

The specification is a blueprint; a few of its code samples are not safe to copy as-is for a real
payment system. Each change is explained in the module guide where it appears.

| Spec | Implemented | Why |
|---|---|---|
| CVV/PIN hashed with plain SHA-256 | HMAC-SHA256 with a secret *pepper* + salted PBKDF2 | A 4-digit PIN has 10,000 values; an unsalted SHA-256 hash is reversed instantly from a DB dump |
| AES key = `secretKey.PadRight(32)` | 32 random bytes, Base64, from configuration / Key Vault | A padded password has far less than 256 bits of entropy |
| AES-CBC for data at rest | AES-256-GCM (authenticated encryption) | Detects tampering; CBC without a MAC does not |
| `computedSig == signature` | `CryptographicOperations.FixedTimeEquals` | `==` leaks timing information (Module 5) |
| One `secretKey` for AES and HMAC | separate `EncryptionKey` and `SigningKey` per partner | one key for two purposes: a weakness in one use breaks both (Module 5) |
| Signature over the payload only | signature over method, path, timestamp, nonce and body; ±5 min window + single-use nonce | a captured request could otherwise be replayed (Module 5) |
| Storing a CVV hash | Kept to follow the schema, but see note below | PCI DSS forbids storing CVV after authorization in production |
| Cashback inside `EmiEngineService`, fixed rates | separate, configurable `CashbackEngine` + minimum spend and per-purchase cap; refunds reverse cashback | single responsibility; spec asks for rules "based on merchant category and spent amount" (Module 3) |
| Client sends the EMI interest rate (`ConvertEmiDto.InterestRate`) | rate set by the bank per tenure (configurable); request carries only the tenure | otherwise a customer could ask for 0 % (Module 4) |
| EMI maths with `double` and `Math.Pow`; schedule principal may not add up | `decimal` maths; last installment repays the exact remainder; 0 % supported | money must add up to the paisa (Module 4) |
| No link between refund and EMI conversion | `TransactionStatus` and `IsEmiConverted` are concurrency tokens; a converted purchase cannot be refunded | prevents "refunded AND converted" (Module 4) |
| No way to search an encrypted card number | `CardNumberHash` blind index (HMAC-SHA256, HKDF-derived key) | AES-GCM with random nonces is not searchable (Module 2) |
| `AvailableBalance <= CreditLimit` (added by us in Module 1) | dropped in Module 2 | a refund after repayment legitimately creates a credit balance |

> **PCI DSS note:** real card issuers must not store the CVV (not even hashed) and normally delegate
> card-number storage to a certified vault/HSM. This project follows the spec's schema for learning
> purposes and points out these differences so you know what changes in a production system.
