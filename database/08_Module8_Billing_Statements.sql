/* =====================================================================================
   Secure Credit EMI Card Service - MODULE 8 : Billing cycle & statements
   Script 08 : CardStatements, "billed by statement" links, bank charges on the ledger
   Run AFTER 01a - 07:
       sqlcmd -S localhost\SQLEXPRESS -E -i database\08_Module8_Billing_Statements.sql
   Safe to run more than once.
   ===================================================================================== */

USE SecureEmiCardDb;
GO

-- Filtered indexes need these settings. SSMS has them ON by default, sqlcmd does not (without -I).
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* -------------------------------------------------------------------------------------
   1. Bank charges on the ledger: Fee (late payment, cash advance), Interest, Tax (GST).
   ------------------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_Transactions_Type' AND definition NOT LIKE N'%Interest%')
    ALTER TABLE dbo.Transactions DROP CONSTRAINT CK_Transactions_Type;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Transactions_Type')
    ALTER TABLE dbo.Transactions ADD CONSTRAINT CK_Transactions_Type
        CHECK (TransactionType IN (N'Swipe', N'Load', N'Refund', N'EmiInstallment', N'Fee', N'Interest', N'Tax'));
GO

/* -------------------------------------------------------------------------------------
   2. A charge is never declined, so it can take a card over its limit (a negative available
      balance). New purchases are then declined until the customer pays. Script 01a required
      AvailableBalance >= 0; that rule is dropped.
   ------------------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CreditCards_AvailableBalance')
    ALTER TABLE dbo.CreditCards DROP CONSTRAINT CK_CreditCards_AvailableBalance;
GO

/* -------------------------------------------------------------------------------------
   3. CreditCards.LastStatementDate : end of the last billing period (NULL = no statement yet).
      The scheduler starts a new statement CycleLength after it (or after issuance).
   ------------------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.CreditCards', N'LastStatementDate') IS NULL
    ALTER TABLE dbo.CreditCards ADD LastStatementDate DATETIME2 NULL;
GO

/* -------------------------------------------------------------------------------------
   4. CardStatements : one row per card per billing cycle.
      Opening + Purchases + CashWithdrawals + FeesAndCharges
        - Payments - Refunds - Cashback - MovedToEmi  = ClosingBalance (the total amount due)
      Status: Open until the due date passes, then Paid | MinimumPaid | Overdue.
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.CardStatements', N'U') IS NULL
CREATE TABLE dbo.CardStatements (
    StatementId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CardStatements PRIMARY KEY,
    CardId              INT            NOT NULL,
    PeriodStart         DATETIME2      NOT NULL,
    PeriodEnd           DATETIME2      NOT NULL,          -- the statement date
    DueDate             DATETIME2      NOT NULL,
    OpeningBalance      DECIMAL(18,2)  NOT NULL,
    Purchases           DECIMAL(18,2)  NOT NULL,
    CashWithdrawals     DECIMAL(18,2)  NOT NULL,
    FeesAndCharges      DECIMAL(18,2)  NOT NULL,
    Payments            DECIMAL(18,2)  NOT NULL,
    Refunds             DECIMAL(18,2)  NOT NULL,
    Cashback            DECIMAL(18,2)  NOT NULL,
    MovedToEmi          DECIMAL(18,2)  NOT NULL,
    ClosingBalance      DECIMAL(18,2)  NOT NULL,          -- total amount due (negative = credit balance)
    MinimumDue          DECIMAL(18,2)  NOT NULL,
    EmiInstallmentsDue  DECIMAL(18,2)  NOT NULL CONSTRAINT DF_CardStatements_EmiDue DEFAULT 0,
    Status              NVARCHAR(20)   NOT NULL CONSTRAINT DF_CardStatements_Status DEFAULT N'Open'
                        CONSTRAINT CK_CardStatements_Status CHECK (Status IN (N'Open', N'Paid', N'MinimumPaid', N'Overdue')),
    PaidByDueDate       DECIMAL(18,2)  NULL,
    AssessedAt          DATETIME2      NULL,              -- when the due-date outcome was decided
    ReminderSentAt      DATETIME2      NULL,
    CONSTRAINT FK_CardStatements_CreditCards FOREIGN KEY (CardId) REFERENCES dbo.CreditCards(CardId),
    CONSTRAINT CK_CardStatements_Period CHECK (PeriodEnd > PeriodStart AND DueDate > PeriodEnd),
    CONSTRAINT CK_CardStatements_Minimum CHECK (MinimumDue >= 0 AND (MinimumDue <= ClosingBalance OR MinimumDue = 0)),
    CONSTRAINT CK_CardStatements_Reconciles CHECK (
        OpeningBalance + Purchases + CashWithdrawals + FeesAndCharges
        - Payments - Refunds - Cashback - MovedToEmi = ClosingBalance),
    CONSTRAINT CK_CardStatements_Assessed CHECK (Status = N'Open' OR AssessedAt IS NOT NULL)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_CardStatements_Card_PeriodEnd')
    CREATE UNIQUE INDEX UX_CardStatements_Card_PeriodEnd ON dbo.CardStatements(CardId, PeriodEnd);
GO
-- The scheduler: statements still waiting for their due-date outcome.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CardStatements_Unassessed')
    CREATE INDEX IX_CardStatements_Unassessed ON dbo.CardStatements(DueDate) WHERE AssessedAt IS NULL;
GO

/* -------------------------------------------------------------------------------------
   5. "Billed by" links. A statement takes everything NOT BILLED YET (StatementId IS NULL) and
      stamps it with its id - so nothing can fall between two statements, whatever the timing.
      Transactions : purchases, cash, payments, refunds, charges
      CashbackLogs : cashback earned / reversed
      EmiPlans     : purchases moved to EMI
      Existing rows stay NULL: the first statement of every card bills its whole history.
   ------------------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.Transactions', N'StatementId') IS NULL
    ALTER TABLE dbo.Transactions ADD StatementId INT NULL
        CONSTRAINT FK_Transactions_CardStatements REFERENCES dbo.CardStatements(StatementId);
GO
IF COL_LENGTH(N'dbo.CashbackLogs', N'StatementId') IS NULL
    ALTER TABLE dbo.CashbackLogs ADD StatementId INT NULL
        CONSTRAINT FK_CashbackLogs_CardStatements REFERENCES dbo.CardStatements(StatementId);
GO
IF COL_LENGTH(N'dbo.EmiPlans', N'StatementId') IS NULL
    ALTER TABLE dbo.EmiPlans ADD StatementId INT NULL
        CONSTRAINT FK_EmiPlans_CardStatements REFERENCES dbo.CardStatements(StatementId);
GO

-- "What is not billed yet for this card?" - filtered, so they only hold the (few) unbilled rows.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Transactions_Unbilled')
    CREATE INDEX IX_Transactions_Unbilled ON dbo.Transactions(CardId) WHERE StatementId IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Transactions_StatementId')
    CREATE INDEX IX_Transactions_StatementId ON dbo.Transactions(StatementId) WHERE StatementId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CashbackLogs_Unbilled')
    CREATE INDEX IX_CashbackLogs_Unbilled ON dbo.CashbackLogs(CardId) WHERE StatementId IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmiPlans_Unbilled')
    CREATE INDEX IX_EmiPlans_Unbilled ON dbo.EmiPlans(CardId) WHERE StatementId IS NULL;
GO

/* -------------------------------------------------------------------------------------
   6. New alert category for statements, reminders and late fees.
   ------------------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_Notifications_Category' AND definition NOT LIKE N'%Billing%')
    ALTER TABLE dbo.Notifications DROP CONSTRAINT CK_Notifications_Category;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Notifications_Category')
    ALTER TABLE dbo.Notifications ADD CONSTRAINT CK_Notifications_Category
        CHECK (Category IN (N'Transaction', N'Card', N'Security', N'Billing'));
GO
