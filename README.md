# Secure Credit EMI Card Service

A secure credit card application built **module by module** from the
*Secure Credit EMI Card Service – Technical Architecture & Specification Report*.

| Layer | Technology |
|---|---|
| Client | Angular 21 (standalone components, signals, Reactive Forms, RxJS, Bootstrap 5) |
| API | ASP.NET Core 8 Web API, JWT Bearer (HMAC-SHA512), Swagger, rate limiting |
| Business | Clean Architecture: Domain → Application → Infrastructure → API, FluentValidation |
| Data | SQL Server 2022 / Azure SQL, EF Core 8 (repository + unit of work) |
| Security | AES-256-GCM (card numbers), HMAC blind index (card lookup), PBKDF2 (passwords), peppered PBKDF2 (CVV/PIN), PIN lockout, optimistic concurrency |

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
└── docs/                            Step-by-step guide, one document per module
```

## Build plan (one module at a time)

| # | Module | Status |
|---|---|---|
| 1 | [Foundation + Cardholder & Card Lifecycle Management](docs/01-Module-1-Cardholders-and-Cards.md) | ✅ Done |
| 2 | [Merchant Swipe & Load Operations (transactions ledger)](docs/02-Module-2-Swipe-and-Load.md) | ✅ Done |
| 3 | Automated Cashback Reward Engine | ⏳ Next |
| 4 | EMI Conversion Engine (amortization + schedules) | ⏳ |
| 5 | Inter-Bank Payload Security (AES payload encryption + HMAC signatures + audit log) | ⏳ |
| 6 | Deployment (Docker, Azure App Service, Key Vault, CI/CD) | ⏳ |

See [docs/00-Roadmap.md](docs/00-Roadmap.md) for the full plan.

## Quick start

**Prerequisites:** .NET 8 SDK, Node.js 22.12+ (or 20.19+), SQL Server 2022 (LocalDB, Developer edition or Docker).

```bash
# 1. Database (run every script in database/ in order – see database/README.md)
sqlcmd -S localhost -E -i database/01a_Database_Schema_Core_Tables.sql
sqlcmd -S localhost -E -i database/02_Module2_Transactions.sql

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

> ⚠️ `appsettings.Development.json` contains **development-only** keys so the project runs out of the box.
> Never reuse them anywhere else. Production keys belong in Azure Key Vault or environment variables.
