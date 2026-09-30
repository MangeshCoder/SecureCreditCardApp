# Database scripts

The SQL scripts are the **source of truth** for the schema. EF Core only maps onto these tables
(it does not create them), which is common in banks where DBAs review every schema change.

Run the scripts in order. Each module adds its own script:

| Script | Module | Tables |
|---|---|---|
| `01a_Database_Schema_Core_Tables.sql` | 1 – Cardholders & Cards | `Cardholders`, `CreditCards` |
| `01b_Database_Schema_EMI_Tables.sql` | 2/4 – Transactions & EMI | *(coming in a later module)* |
| `01c_Schedules_Cashback_Audit_Indexes.sql` | 3/4/5 – Schedules, Cashback, Audit | *(coming in a later module)* |

```bash
# Windows auth, local SQL Server
sqlcmd -S localhost -E -i database/01a_Database_Schema_Core_Tables.sql

# SQL Server in Docker (sa login)
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Your_strong_Passw0rd" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
sqlcmd -S localhost -U sa -P "Your_strong_Passw0rd" -C -i database/01a_Database_Schema_Core_Tables.sql
```

The scripts are idempotent (`IF ... IS NULL`), so running them again is safe.
