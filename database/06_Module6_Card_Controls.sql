/* =====================================================================================
   Secure Credit EMI Card Service - MODULE 6 : Card Controls & Spending Limits
   Script 06 : CardControls table, temporary lock on CreditCards, channel + country on Transactions
   Run AFTER 01a, 02, 03, 04 and 05:
       sqlcmd -S localhost\SQLEXPRESS -E -i database\06_Module6_Card_Controls.sql
   Safe to run more than once.
   ===================================================================================== */

USE SecureEmiCardDb;
GO

/* -------------------------------------------------------------------------------------
   1. CreditCards.IsLocked / LockedAt : temporary lock by the cardholder.
      Different from CardStatus = 'Blocked': a block is the bank's decision (lost/stolen card,
      3 wrong PINs) and only the bank can undo it; a lock is switched on and off by the customer.
   ------------------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.CreditCards', N'IsLocked') IS NULL
    ALTER TABLE dbo.CreditCards ADD IsLocked BIT NOT NULL
        CONSTRAINT DF_CreditCards_IsLocked DEFAULT 0;
GO

IF COL_LENGTH(N'dbo.CreditCards', N'LockedAt') IS NULL
    ALTER TABLE dbo.CreditCards ADD LockedAt DATETIME2 NULL;
GO

/* -------------------------------------------------------------------------------------
   2. Transactions : HOW and WHERE the card was used.
      Channel         : Pos (chip + PIN at a shop) | Online | Contactless | Atm (cash)
                        NULL for rows that are not card usage (repayments, EMI installments)
      MerchantCountry : ISO 3166 alpha-2, e.g. IN, US
      IsInternational : stored, so daily limits don't depend on today's configuration
   ------------------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.Transactions', N'Channel') IS NULL
    ALTER TABLE dbo.Transactions ADD Channel NVARCHAR(20) NULL
        CONSTRAINT CK_Transactions_Channel CHECK (Channel IN (N'Pos', N'Online', N'Contactless', N'Atm'));
GO

IF COL_LENGTH(N'dbo.Transactions', N'MerchantCountry') IS NULL
    ALTER TABLE dbo.Transactions ADD MerchantCountry NCHAR(2) NULL;
GO

IF COL_LENGTH(N'dbo.Transactions', N'IsInternational') IS NULL
    ALTER TABLE dbo.Transactions ADD IsInternational BIT NOT NULL
        CONSTRAINT DF_Transactions_IsInternational DEFAULT 0;
GO

-- Every purchase before Module 6 was made with chip + PIN at a shop.
UPDATE dbo.Transactions
SET Channel = N'Pos'
WHERE Channel IS NULL
  AND TransactionType IN (N'Swipe', N'Refund');
GO

/* -------------------------------------------------------------------------------------
   3. CardControls : the cardholder's switches and daily limits, one row per card.
      CardId is both the primary key and the foreign key (1:1 with CreditCards).
      Defaults follow the RBI rule for new cards: only contact-based use in India (shop + ATM).
      A NULL daily limit means "no extra limit" - only the available credit applies.
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.CardControls', N'U') IS NULL
CREATE TABLE dbo.CardControls (
    CardId                   INT            NOT NULL CONSTRAINT PK_CardControls PRIMARY KEY,
    PosEnabled               BIT            NOT NULL CONSTRAINT DF_CardControls_PosEnabled DEFAULT 1,
    OnlineEnabled            BIT            NOT NULL CONSTRAINT DF_CardControls_OnlineEnabled DEFAULT 0,
    ContactlessEnabled       BIT            NOT NULL CONSTRAINT DF_CardControls_ContactlessEnabled DEFAULT 0,
    AtmEnabled               BIT            NOT NULL CONSTRAINT DF_CardControls_AtmEnabled DEFAULT 1,
    InternationalEnabled     BIT            NOT NULL CONSTRAINT DF_CardControls_InternationalEnabled DEFAULT 0,
    PosDailyLimit            DECIMAL(18,2)  NULL,
    OnlineDailyLimit         DECIMAL(18,2)  NULL,
    ContactlessDailyLimit    DECIMAL(18,2)  NULL,
    AtmDailyLimit            DECIMAL(18,2)  NULL,
    InternationalDailyLimit  DECIMAL(18,2)  NULL,
    UpdatedAt                DATETIME2      NOT NULL CONSTRAINT DF_CardControls_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_CardControls_CreditCards FOREIGN KEY (CardId)
        REFERENCES dbo.CreditCards(CardId) ON DELETE CASCADE,
    CONSTRAINT CK_CardControls_Limits CHECK (
            (PosDailyLimit           IS NULL OR PosDailyLimit > 0)
        AND (OnlineDailyLimit        IS NULL OR OnlineDailyLimit > 0)
        AND (ContactlessDailyLimit   IS NULL OR ContactlessDailyLimit > 0)
        AND (AtmDailyLimit           IS NULL OR AtmDailyLimit > 0)
        AND (InternationalDailyLimit IS NULL OR InternationalDailyLimit > 0))
);
GO

/* -------------------------------------------------------------------------------------
   4. Existing cards get the default controls.
      (New cards get their row from the API at issuance.)
   ------------------------------------------------------------------------------------- */
INSERT INTO dbo.CardControls (CardId)
SELECT c.CardId
FROM dbo.CreditCards c
WHERE NOT EXISTS (SELECT 1 FROM dbo.CardControls cc WHERE cc.CardId = c.CardId);
GO
