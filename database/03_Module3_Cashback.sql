/* =====================================================================================
   Secure Credit EMI Card Service - MODULE 3 : Automated Cashback Reward Engine
   Script 03 : CashbackLogs (the cashback ledger)
   Run AFTER 01a and 02:
       sqlcmd -S localhost\SQLEXPRESS -E -i database\03_Module3_Cashback.sql
   Safe to run more than once.
   ===================================================================================== */

USE SecureEmiCardDb;
GO

/* -------------------------------------------------------------------------------------
   CashbackLogs : one row per cashback movement. Like the Transactions ledger, rows are never
   updated or deleted.
     CashbackType = 'Earned'   : credited for an approved swipe        (CashbackAmount > 0)
     CashbackType = 'Reversed' : taken back when that swipe is refunded (CashbackAmount < 0)
   Because a reversal is stored as a NEGATIVE amount, SUM(CashbackAmount) is always the net
   cashback - no CASE expressions needed in reports.
   TransactionId always points to the ORIGINAL swipe, so UNIQUE (TransactionId, CashbackType)
   guarantees a swipe earns cashback at most once and is reversed at most once.
   (Spec columns + CashbackType and the FK to CreditCards, which the spec script did not have.)
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.CashbackLogs', N'U') IS NULL
CREATE TABLE dbo.CashbackLogs (
    CashbackId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashbackLogs PRIMARY KEY,
    TransactionId       INT            NOT NULL,
    CardId              INT            NOT NULL,
    CashbackPercentage  DECIMAL(5,2)   NOT NULL
                        CONSTRAINT CK_CashbackLogs_Percentage CHECK (CashbackPercentage >= 0 AND CashbackPercentage <= 100),
    CashbackAmount      DECIMAL(18,2)  NOT NULL,
    CashbackType        NVARCHAR(20)   NOT NULL CONSTRAINT DF_CashbackLogs_Type DEFAULT N'Earned',
    CreditedDate        DATETIME2      NOT NULL CONSTRAINT DF_CashbackLogs_CreditedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_CashbackLogs_Transactions FOREIGN KEY (TransactionId) REFERENCES dbo.Transactions(TransactionId),
    CONSTRAINT FK_CashbackLogs_CreditCards  FOREIGN KEY (CardId)        REFERENCES dbo.CreditCards(CardId),
    CONSTRAINT CK_CashbackLogs_TypeAndSign CHECK (
        (CashbackType = N'Earned'   AND CashbackAmount > 0) OR
        (CashbackType = N'Reversed' AND CashbackAmount < 0)),
    CONSTRAINT UQ_CashbackLogs_Transaction_Type UNIQUE (TransactionId, CashbackType)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CashbackLogs_CardId_Date')
    CREATE INDEX IX_CashbackLogs_CardId_Date ON dbo.CashbackLogs(CardId, CreditedDate DESC);
GO
