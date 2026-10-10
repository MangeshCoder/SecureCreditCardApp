# Secure Credit EMI Card Service

A secure credit card application built **module by module** from the
*Secure Credit EMI Card Service – Technical Architecture & Specification Report*.

| Layer | Technology |
|---|---|
| Client | Angular 21 (standalone components, signals, Reactive Forms, RxJS, Bootstrap 5) |
| API | ASP.NET Core 8 Web API, JWT Bearer (HMAC-SHA512), Swagger, rate limiting |
| Business | Clean Architecture: Domain → Application → Infrastructure → API, FluentValidation |
| Data | SQL Server 2022 / Azure SQL, EF Core 8 (repository + unit of work) |
| Security | AES-256-GCM (card numbers), HMAC blind index (card lookup), PBKDF2 (passwords), peppered PBKDF2 (CVV/PIN), PIN lockout, optimistic concurrency, signed + encrypted partner-bank gateway, OTP step-up authentication, append-only audit log |

## Repository layout

```
SecureCreditCardApp/
├── backend/                         ASP.NET Core solution (SecureEmiCard.sln)
│   ├── src/SecureEmiCard.Domain          Entities, enums, business rules (no dependencies)
│   ├── src/SecureEmiCard.Application     Use cases, DTOs, validators, interfaces
│   ├── src/SecureEmiCard.Infrastructure  EF Core, repositories, crypto, JWT
│   ├── src/SecureEmiCard.Api             Controllers, middleware, Program.cs
│   └── tests/SecureEmiCard.UnitTests     xUnit tests
├── database/                        SQL Server scripts (source of truth for the schema)
├── frontend/                        Angular client
├── tools/PartnerBankSimulator/      Console app that plays a partner bank calling the gateway (Module 5)
└── docs/                            Step-by-step guide, one document per module
```

## Build plan (one module at a time)

| # | Module | Status |
|---|---|---|
| 1 | [Foundation + Cardholder & Card Lifecycle Management](docs/01-Module-1-Cardholders-and-Cards.md) | ✅ Done |
| 2 | [Merchant Swipe & Load Operations (transactions ledger)](docs/02-Module-2-Swipe-and-Load.md) | ✅ Done |
| 3 | [Automated Cashback Reward Engine](docs/03-Module-3-Cashback.md) | ✅ Done |
| 4 | [EMI Conversion Engine (amortization + schedules)](docs/04-Module-4-Emi.md) | ✅ Done |
| 5 | [Inter-Bank Payload Security + Security Audit Log](docs/05-Module-5-InterBank-Security-Audit.md) | ✅ Done |
| 6 | [Card Controls & Spending Limits (channels, international, daily limits, temporary lock)](docs/06-Module-6-Card-Controls.md) | ✅ Done |
| 7 | [OTP / Two-Factor Authentication + Notifications (step-up codes, alerts, SMS/e-mail outbox)](docs/07-Module-7-Otp-Notifications.md) | ✅ Done |
| 8 | Billing cycle & statements (minimum due, due date, late fee, interest, PDF) | ⏳ Next |
| 9 | EMI extras (processing fee + GST, foreclosure, Key Fact Statement) | ⏳ |
| 10 | Fraud detection rules | ⏳ |
| 11 | Rewards 2.0 (points, milestones, redemption) | ⏳ |
| 12 | Deployment (Docker, Azure App Service, Key Vault, CI/CD) | ⏳ |

See [docs/00-Roadmap.md](docs/00-Roadmap.md) for the full plan.

## Quick start

**Prerequisites:** .NET 8 SDK, Node.js 22.12+ (or 20.19+), SQL Server 2022 (LocalDB, Developer edition or Docker).

```bash
# 1. Database (run every script in database/ in order – see database/README.md)
sqlcmd -S localhost -E -i database/01a_Database_Schema_Core_Tables.sql
sqlcmd -S localhost -E -i database/02_Module2_Transactions.sql
sqlcmd -S localhost -E -i database/03_Module3_Cashback.sql
sqlcmd -S localhost -E -i database/04_Module4_Emi.sql
sqlcmd -S localhost -E -i database/05_Module5_Security_Audit.sql
sqlcmd -S localhost -E -i database/06_Module6_Card_Controls.sql
sqlcmd -S localhost -E -i database/07_Module7_Otp_Notifications.sql

# 2. API  -> http://localhost:5080/swagger
cd backend
dotnet test
dotnet run --project src/SecureEmiCard.Api --launch-profile http

# 3. Angular -> http://localhost:4200
cd frontend
npm install
npm start
```

No SQL Server yet? Run the API against an in-memory database:

```bash
Database__UseInMemory=true dotnet run --project src/SecureEmiCard.Api --launch-profile http      # bash
$env:Database__UseInMemory="true"; dotnet run --project src/SecureEmiCard.Api --launch-profile http  # PowerShell
```

Development admin login (seeded on start-up): `admin@secureemi.local` / `Admin@12345`.
Since Module 7 the admin sign-in also asks for a one-time code: in development the dialog shows the SMS,
and the yellow **phone** button in the navbar opens the phone simulator (all SMS and e-mails the API "sent").

> ⚠️ `appsettings.Development.json` contains **development-only** keys so the project runs out of the box.
> Never reuse them anywhere else. Production keys belong in Azure Key Vault or environment variables.
