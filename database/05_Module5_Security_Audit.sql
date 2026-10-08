/* =====================================================================================
   Secure Credit EMI Card Service - MODULE 5 : Inter-Bank Payload Security + Audit Log
   Script 05 : SecurityAuditLogs (append-only)
   Run AFTER 01a, 02, 03 and 04:
       sqlcmd -S localhost\SQLEXPRESS -E -i database\05_Module5_Security_Audit.sql
   Safe to run more than once.
   ===================================================================================== */

USE SecureEmiCardDb;
GO

/* -------------------------------------------------------------------------------------
   1. SecurityAuditLogs (spec script 01c) - who did what, when, from where, and was it allowed.
      Spec columns : AuditId, Endpoint, ActionType, SignatureValid, PayloadHash, Timestamp
      Added        : Outcome, HttpStatus, UserId, PartnerId, ClientIp, CorrelationId, Detail
      Outcome      : Success  = the operation was carried out
                     Rejected = a SECURITY check refused it (bad signature, replay, 401/403, rate limit)
                     Failed   = it was allowed but failed (validation or business rule, server error)
      PayloadHash  : SHA-256 of the raw (encrypted) gateway body - proves WHICH payload was received
                     without storing card data. Never a hash of PINs/CVVs (small value spaces can be brute-forced).
      No foreign keys on purpose: an audit row must survive even if the user or card is later removed.
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.SecurityAuditLogs', N'U') IS NULL
CREATE TABLE dbo.SecurityAuditLogs (
    AuditId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SecurityAuditLogs PRIMARY KEY,
    Endpoint        NVARCHAR(250)  NOT NULL,
    ActionType      NVARCHAR(50)   NOT NULL,
    SignatureValid  BIT            NOT NULL,
    PayloadHash     NVARCHAR(256)  NOT NULL,
    Timestamp       DATETIME2      NOT NULL CONSTRAINT DF_SecurityAuditLogs_Timestamp DEFAULT SYSUTCDATETIME(),
    Outcome         NVARCHAR(20)   NOT NULL
                    CONSTRAINT CK_SecurityAuditLogs_Outcome CHECK (Outcome IN (N'Success', N'Rejected', N'Failed')),
    HttpStatus      INT            NULL,
    UserId          INT            NULL,
    PartnerId       NVARCHAR(50)   NULL,
    ClientIp        NVARCHAR(45)   NULL,       -- 45 = longest IPv6 text form
    CorrelationId   NVARCHAR(64)   NULL,       -- ASP.NET Core TraceIdentifier, to match application logs
    Detail          NVARCHAR(250)  NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SecurityAuditLogs_Timestamp')
    CREATE INDEX IX_SecurityAuditLogs_Timestamp ON dbo.SecurityAuditLogs(Timestamp DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SecurityAuditLogs_ActionType_Timestamp')
    CREATE INDEX IX_SecurityAuditLogs_ActionType_Timestamp ON dbo.SecurityAuditLogs(ActionType, Timestamp DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SecurityAuditLogs_Outcome_Timestamp')
    CREATE INDEX IX_SecurityAuditLogs_Outcome_Timestamp ON dbo.SecurityAuditLogs(Outcome, Timestamp DESC);
GO

/* -------------------------------------------------------------------------------------
   2. Append-only: an audit trail that can be edited proves nothing.
      This INSTEAD OF trigger turns every UPDATE or DELETE into an error - even for a DBA
      running a query by hand. (CREATE TRIGGER must be alone in its batch, hence EXEC.)
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.TR_SecurityAuditLogs_AppendOnly', N'TR') IS NULL
    EXEC (N'CREATE TRIGGER dbo.TR_SecurityAuditLogs_AppendOnly
            ON dbo.SecurityAuditLogs
            INSTEAD OF UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 50001, N''SecurityAuditLogs is append-only: rows cannot be updated or deleted.'', 1;
            END');
GO