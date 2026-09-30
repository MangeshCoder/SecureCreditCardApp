/* =====================================================================================
   Secure Credit EMI Card Service - MODULE 2 : Merchant Swipe & Load Operations
   Script 02 : Transactions ledger + CreditCards changes needed for swipes
   Run AFTER 01a:
       sqlcmd -S localhost\SQLEXPRESS -E -i database\02_Module2_Transactions.sql
   Safe to run more than once.
   ===================================================================================== */

USE SecureEmiCardDb;
GO

/* -------------------------------------------------------------------------------------
   1. CreditCards.CardNumberHash  ("blind index")
      The card number is AES-GCM encrypted with a random nonce, so the same number never
      produces the same cipher text and cannot be searched with WHERE. When a merchant sends
      a card number we compute HMAC-SHA256(secret key, cardNumber) and look up this column.
      NULL is allowed so cards issued in Module 1 can be back-filled at API start-up.
   ------------------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.CreditCards', N'CardNumberHash') IS NULL
    ALTER TABLE dbo.CreditCards ADD CardNumberHash NVARCHAR(64) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_CreditCards_CardNumberHash')
    CREATE UNIQUE INDEX UX_CreditCards_CardNumberHash
        ON dbo.CreditCards(CardNumberHash)
        WHERE CardNumberHash IS NOT NULL;
GO

/* -------------------------------------------------------------------------------------
   2. CreditCards.FailedPinAttempts
      Counts consecutive wrong PINs. At 3 the card is blocked automatically.
   ------------------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.CreditCards', N'FailedPinAttempts') IS NULL
    ALTER TABLE dbo.CreditCards ADD FailedPinAttempts INT NOT NULL
        CONSTRAINT DF_CreditCards_FailedPinAttempts DEFAULT 0
        CONSTRAINT CK_CreditCards_FailedPinAttempts CHECK (FailedPinAttempts >= 0);
GO

/* -------------------------------------------------------------------------------------
   3. Allow a credit balance.
      Module 1 required AvailableBalance <= CreditLimit. A merchant refund of a purchase that the
      customer has already repaid legitimately pushes the balance above the limit (the bank owes the
      customer), so that constraint is removed. AvailableBalance >= 0 still applies.
   ------------------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CreditCards_Balance_Within_Limit')
    ALTER TABLE dbo.CreditCards DROP CONSTRAINT CK_CreditCards_Balance_Within_Limit;
GO

/* -------------------------------------------------------------------------------------
   4. Transactions : the ledger. Every swipe (approved or declined), load and refund.
      TransactionType   : Swipe  = purchase at a merchant   (reduces AvailableBalance)
                          Load   = repayment / balance load  (increases AvailableBalance)
                          Refund = merchant refund of a swipe (increases AvailableBalance)
      TransactionStatus : Completed | Declined | Refunded
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NULL
CREATE TABLE dbo.Transactions (
    TransactionId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Transactions PRIMARY KEY,
    CardId                INT            NOT NULL,
    MerchantName          NVARCHAR(100)  NOT NULL,
    MerchantCategoryCode  NVARCHAR(10)   NOT NULL,
    Amount                DECIMAL(18,2)  NOT NULL CONSTRAINT CK_Transactions_Amount CHECK (Amount > 0),
    TransactionType       NVARCHAR(20)   NOT NULL
                          CONSTRAINT CK_Transactions_Type CHECK (TransactionType IN (N'Swipe', N'Load', N'Refund')),
    TransactionStatus     NVARCHAR(20)   NOT NULL CONSTRAINT DF_Transactions_Status DEFAULT N'Completed'
                          CONSTRAINT CK_Transactions_Status CHECK (TransactionStatus IN (N'Completed', N'Declined', N'Refunded')),
    IsEmiConverted        BIT            NOT NULL CONSTRAINT DF_Transactions_IsEmiConverted DEFAULT 0,
    TransactionDate       DATETIME2      NOT NULL CONSTRAINT DF_Transactions_TransactionDate DEFAULT SYSUTCDATETIME(),
    DigitalSignature      NVARCHAR(512)  NULL,          -- filled by Module 5 (inter-bank signatures)
    DeclineReason         NVARCHAR(100)  NULL,          -- why a swipe was declined (added in Module 2)
    CONSTRAINT FK_Transactions_CreditCards FOREIGN KEY (CardId) REFERENCES dbo.CreditCards(CardId)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Transactions_CardId_Date')
    CREATE INDEX IX_Transactions_CardId_Date ON dbo.Transactions(CardId, TransactionDate DESC);
GO
