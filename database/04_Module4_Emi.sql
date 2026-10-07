/* =====================================================================================
   Secure Credit EMI Card Service - MODULE 4 : EMI Conversion Engine
   Script 04 : EmiPlans + EmiSchedules, and the 'EmiInstallment' transaction type
   Run AFTER 01a, 02 and 03:
       sqlcmd -S localhost\SQLEXPRESS -E -i database\04_Module4_Emi.sql
   Safe to run more than once.
   ===================================================================================== */

USE SecureEmiCardDb;
GO

/* -------------------------------------------------------------------------------------
   1. A new ledger entry type: paying an EMI installment.
      The CHECK constraint from script 02 is re-created with the extra value.
   ------------------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_Transactions_Type'
             AND definition NOT LIKE N'%EmiInstallment%')
    ALTER TABLE dbo.Transactions DROP CONSTRAINT CK_Transactions_Type;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Transactions_Type')
    ALTER TABLE dbo.Transactions ADD CONSTRAINT CK_Transactions_Type
        CHECK (TransactionType IN (N'Swipe', N'Load', N'Refund', N'EmiInstallment'));
GO

/* -------------------------------------------------------------------------------------
   2. EmiPlans : one plan per converted purchase (spec script 01b).
      RemainingBalance = what is still to be paid on the plan (sum of unpaid installments).
      TransactionId is UNIQUE: a purchase can be converted only once.
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.EmiPlans', N'U') IS NULL
CREATE TABLE dbo.EmiPlans (
    EmiPlanId           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmiPlans PRIMARY KEY,
    TransactionId       INT            NOT NULL CONSTRAINT UQ_EmiPlans_TransactionId UNIQUE,
    CardId              INT            NOT NULL,
    PrincipalAmount     DECIMAL(18,2)  NOT NULL CONSTRAINT CK_EmiPlans_Principal CHECK (PrincipalAmount > 0),
    TenureMonths        INT            NOT NULL CONSTRAINT CK_EmiPlans_Tenure CHECK (TenureMonths IN (3, 6, 12, 24)),
    AnnualInterestRate  DECIMAL(5,2)   NOT NULL CONSTRAINT CK_EmiPlans_Rate CHECK (AnnualInterestRate >= 0 AND AnnualInterestRate <= 60),
    MonthlyInstallment  DECIMAL(18,2)  NOT NULL CONSTRAINT CK_EmiPlans_Installment CHECK (MonthlyInstallment > 0),
    TotalRepayable      DECIMAL(18,2)  NOT NULL,
    RemainingBalance    DECIMAL(18,2)  NOT NULL CONSTRAINT CK_EmiPlans_Remaining CHECK (RemainingBalance >= 0),
    PlanStatus          NVARCHAR(20)   NOT NULL CONSTRAINT DF_EmiPlans_Status DEFAULT N'Active'
                        CONSTRAINT CK_EmiPlans_Status CHECK (PlanStatus IN (N'Active', N'Closed')),
    CreatedDate         DATETIME2      NOT NULL CONSTRAINT DF_EmiPlans_CreatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_EmiPlans_Transactions FOREIGN KEY (TransactionId) REFERENCES dbo.Transactions(TransactionId),
    CONSTRAINT FK_EmiPlans_CreditCards  FOREIGN KEY (CardId)        REFERENCES dbo.CreditCards(CardId),
    CONSTRAINT CK_EmiPlans_Total CHECK (TotalRepayable >= PrincipalAmount)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmiPlans_CardId')
    CREATE INDEX IX_EmiPlans_CardId ON dbo.EmiPlans(CardId);
GO

/* -------------------------------------------------------------------------------------
   3. EmiSchedules : the amortization schedule, one row per monthly installment (spec 01c).
      AmountDue = PrincipalComponent + InterestComponent.
      PaymentTransactionId (not in the spec) links a paid installment to its ledger row.
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.EmiSchedules', N'U') IS NULL
CREATE TABLE dbo.EmiSchedules (
    ScheduleId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmiSchedules PRIMARY KEY,
    EmiPlanId             INT            NOT NULL,
    InstallmentNumber     INT            NOT NULL CONSTRAINT CK_EmiSchedules_Number CHECK (InstallmentNumber BETWEEN 1 AND 24),
    DueDate               DATE           NOT NULL,
    AmountDue             DECIMAL(18,2)  NOT NULL,
    PrincipalComponent    DECIMAL(18,2)  NOT NULL CONSTRAINT CK_EmiSchedules_Principal CHECK (PrincipalComponent >= 0),
    InterestComponent     DECIMAL(18,2)  NOT NULL CONSTRAINT CK_EmiSchedules_Interest CHECK (InterestComponent >= 0),
    PaymentStatus         NVARCHAR(20)   NOT NULL CONSTRAINT DF_EmiSchedules_Status DEFAULT N'Pending'
                          CONSTRAINT CK_EmiSchedules_Status CHECK (PaymentStatus IN (N'Pending', N'Paid')),
    PaidDate              DATETIME2      NULL,
    PaymentTransactionId  INT            NULL,
    CONSTRAINT FK_EmiSchedules_EmiPlans     FOREIGN KEY (EmiPlanId)            REFERENCES dbo.EmiPlans(EmiPlanId),
    CONSTRAINT FK_EmiSchedules_Transactions FOREIGN KEY (PaymentTransactionId) REFERENCES dbo.Transactions(TransactionId),
    CONSTRAINT UQ_EmiSchedules_Plan_Number UNIQUE (EmiPlanId, InstallmentNumber),
    CONSTRAINT CK_EmiSchedules_Amount CHECK (AmountDue = PrincipalComponent + InterestComponent),
    CONSTRAINT CK_EmiSchedules_Paid CHECK (PaymentStatus <> N'Paid' OR PaidDate IS NOT NULL)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmiSchedules_Plan_Status')
    CREATE INDEX IX_EmiSchedules_Plan_Status ON dbo.EmiSchedules(EmiPlanId, PaymentStatus);
GO
