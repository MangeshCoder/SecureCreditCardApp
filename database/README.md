# Database scripts

The SQL scripts are the **source of truth** for the schema. EF Core only maps onto these tables
(it does not create them), which is common in banks where DBAs review every schema change.

Run the scripts **in order**. Each module adds its own script, and every script is idempotent
(safe to run again).

| Script | Module | What it does |
|---|---|---|
| `01a_Database_Schema_Core_Tables.sql` | 1 – Cardholders & Cards | creates the database, `Cardholders`, `CreditCards` |
| `02_Module2_Transactions.sql` | 2 – Swipe & Load | adds `CreditCards.CardNumberHash` + `FailedPinAttempts`, creates `Transactions` |
| `03_Module3_Cashback.sql` | 3 – Cashback | creates `CashbackLogs` |
| `04_Module4_Emi.sql` | 4 – EMI | creates `EmiPlans`, `EmiSchedules`; adds the `EmiInstallment` transaction type |
| `05_Module5_Security_Audit.sql` | 5 – Inter-bank security | creates the append-only `SecurityAuditLogs` (+ trigger) |
| `06_Module6_Card_Controls.sql` | 6 – Card controls | creates `CardControls`; adds `CreditCards.IsLocked` and `Transactions.Channel` / `MerchantCountry` / `IsInternational` |

```powershell
# SQL Express with Windows login (adjust the server name to yours)
sqlcmd -S localhost\SQLEXPRESS -E -i database\01a_Database_Schema_Core_Tables.sql
sqlcmd -S localhost\SQLEXPRESS -E -i database\02_Module2_Transactions.sql
sqlcmd -S localhost\SQLEXPRESS -E -i database\03_Module3_Cashback.sql
sqlcmd -S localhost\SQLEXPRESS -E -i database\04_Module4_Emi.sql
sqlcmd -S localhost\SQLEXPRESS -E -i database\05_Module5_Security_Audit.sql
sqlcmd -S localhost\SQLEXPRESS -E -i database\06_Module6_Card_Controls.sql
```

Or open the file in SSMS and press **Execute** (F5).
