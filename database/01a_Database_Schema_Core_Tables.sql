/* =====================================================================================
   Secure Credit EMI Card Service - MODULE 1
   Script 01a : Database + Core User & Card Management tables
   Run in SSMS / Azure Data Studio / sqlcmd:
       sqlcmd -S localhost -E -i 01a_Database_Schema_Core_Tables.sql
   ===================================================================================== */

IF DB_ID(N'SecureEmiCardDb') IS NULL
    CREATE DATABASE SecureEmiCardDb;
GO

USE SecureEmiCardDb;
GO

/* -------------------------------------------------------------------------------------
   Cardholders : customers (and bank administrators) who log into the system.
   PasswordHash : PBKDF2-HMAC-SHA256, 600,000 iterations, random salt
                  format  PBKDF2-SHA256$<iterations>$<salt>$<hash>
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Cardholders', N'U') IS NULL
CREATE TABLE dbo.Cardholders (
    CardholderId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cardholders PRIMARY KEY,
    FirstName     NVARCHAR(50)  NOT NULL,
    LastName      NVARCHAR(50)  NOT NULL,
    Email         NVARCHAR(100) NOT NULL CONSTRAINT UQ_Cardholders_Email UNIQUE,
    PhoneNumber   NVARCHAR(20)  NOT NULL,
    PasswordHash  NVARCHAR(256) NOT NULL,
    Role          NVARCHAR(20)  NOT NULL CONSTRAINT DF_Cardholders_Role DEFAULT N'Cardholder'
                  CONSTRAINT CK_Cardholders_Role CHECK (Role IN (N'Cardholder', N'Admin')),
    CreatedAt     DATETIME2     NOT NULL CONSTRAINT DF_Cardholders_CreatedAt DEFAULT SYSUTCDATETIME(),
    IsActive      BIT           NOT NULL CONSTRAINT DF_Cardholders_IsActive DEFAULT 1
);
GO

/* -------------------------------------------------------------------------------------
   CreditCards : one row per issued card.
   CardNumberEncrypted : AES-256-GCM,  format  v1:<base64(nonce|tag|cipher)>
   MaskedCardNumber    : 'XXXX-XXXX-XXXX-1234' - safe to display / search
   CvvHash, PinHash    : HMAC-SHA256 pepper + PBKDF2 (see PepperedSecretHasher.cs)
   AvailableBalance    : CreditLimit - outstanding spend (updated by Module 2 swipes)
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.CreditCards', N'U') IS NULL
CREATE TABLE dbo.CreditCards (
    CardId               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CreditCards PRIMARY KEY,
    CardholderId         INT            NOT NULL,
    CardNumberEncrypted  NVARCHAR(512)  NOT NULL,
    MaskedCardNumber     NVARCHAR(20)   NOT NULL,
    CvvHash              NVARCHAR(256)  NOT NULL,
    PinHash              NVARCHAR(256)  NOT NULL,
    CreditLimit          DECIMAL(18,2)  NOT NULL CONSTRAINT CK_CreditCards_CreditLimit CHECK (CreditLimit >= 0),
    AvailableBalance     DECIMAL(18,2)  NOT NULL CONSTRAINT CK_CreditCards_AvailableBalance CHECK (AvailableBalance >= 0),
    CardStatus           NVARCHAR(20)   NOT NULL CONSTRAINT DF_CreditCards_CardStatus DEFAULT N'Active'
                         CONSTRAINT CK_CreditCards_CardStatus CHECK (CardStatus IN (N'Active', N'Blocked')),
    ExpiryDate           DATE           NOT NULL,
    CreatedAt            DATETIME2      NOT NULL CONSTRAINT DF_CreditCards_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_CreditCards_Cardholders FOREIGN KEY (CardholderId) REFERENCES dbo.Cardholders(CardholderId),
    CONSTRAINT CK_CreditCards_Balance_Within_Limit CHECK (AvailableBalance <= CreditLimit)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CreditCards_CardholderId')
    CREATE INDEX IX_CreditCards_CardholderId ON dbo.CreditCards(CardholderId);
GO
