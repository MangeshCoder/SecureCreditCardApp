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
| `04_...` | 4 – EMI | *(coming)* `EmiPlans`, `EmiSchedules` |
| `05_...` | 5 – Inter-bank security | *(coming)* `SecurityAuditLogs` |

```powershell
# SQL Express with Windows login (adjust the server name to yours)
sqlcmd -S localhost\SQLEXPRESS -E -i database\01a_Database_Schema_Core_Tables.sql
sqlcmd -S localhost\SQLEXPRESS -E -i database\02_Module2_Transactions.sql
sqlcmd -S localhost\SQLEXPRESS -E -i database\03_Module3_Cashback.sql
```

Or open the file in SSMS and press **Execute** (F5).
