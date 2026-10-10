/* =====================================================================================
   Secure Credit EMI Card Service - MODULE 7 : OTP / two-factor authentication + notifications
   Script 07 : OtpChallenges, Notifications, and the 'Challenged' audit outcome
   Run AFTER 01a - 06:
       sqlcmd -S localhost\SQLEXPRESS -E -i database\07_Module7_Otp_Notifications.sql
   Safe to run more than once.
   ===================================================================================== */

USE SecureEmiCardDb;
GO

-- Filtered indexes need these settings. SSMS has them ON by default, sqlcmd does not (without -I).
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* -------------------------------------------------------------------------------------
   1. OtpChallenges : one row per one-time code sent.
      The code itself is NEVER stored - only a peppered hash (like the PIN).
      ContextHash  : SHA-256 of WHAT the code approves (purpose + card / amount / merchant / settings),
                     so a code sent for one action cannot approve another one.
      Status       : Pending | Used (correct code, single use) | Failed (too many wrong codes)
                     | Superseded (a newer code was sent for the same action)
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.OtpChallenges', N'U') IS NULL
CREATE TABLE dbo.OtpChallenges (
    OtpChallengeId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OtpChallenges PRIMARY KEY,
    CardholderId    INT            NOT NULL,
    Purpose         NVARCHAR(30)   NOT NULL
                    CONSTRAINT CK_OtpChallenges_Purpose CHECK (Purpose IN
                        (N'Login', N'OnlinePayment', N'UnlockCard', N'CardControls', N'ChangePin', N'RevealCardNumber')),
    ContextHash     NVARCHAR(64)   NOT NULL,
    CodeHash        NVARCHAR(256)  NOT NULL,
    SentTo          NVARCHAR(30)   NOT NULL,          -- masked, e.g. +91******6072
    Status          NVARCHAR(20)   NOT NULL CONSTRAINT DF_OtpChallenges_Status DEFAULT N'Pending'
                    CONSTRAINT CK_OtpChallenges_Status CHECK (Status IN (N'Pending', N'Used', N'Failed', N'Superseded')),
    Attempts        INT            NOT NULL CONSTRAINT DF_OtpChallenges_Attempts DEFAULT 0,
    MaxAttempts     INT            NOT NULL,
    CreatedAt       DATETIME2      NOT NULL,
    ExpiresAt       DATETIME2      NOT NULL,
    UsedAt          DATETIME2      NULL,
    CONSTRAINT FK_OtpChallenges_Cardholders FOREIGN KEY (CardholderId) REFERENCES dbo.Cardholders(CardholderId),
    CONSTRAINT CK_OtpChallenges_Attempts CHECK (Attempts >= 0 AND MaxAttempts >= 1 AND Attempts <= MaxAttempts),
    CONSTRAINT CK_OtpChallenges_Expiry CHECK (ExpiresAt > CreatedAt),
    CONSTRAINT CK_OtpChallenges_Used CHECK (Status <> N'Used' OR UsedAt IS NOT NULL)
);
GO

-- "How many codes did this person get in the last 15 minutes?" (flood protection)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OtpChallenges_Cardholder_Created')
    CREATE INDEX IX_OtpChallenges_Cardholder_Created ON dbo.OtpChallenges(CardholderId, CreatedAt DESC);
GO

/* -------------------------------------------------------------------------------------
   2. Notifications : the in-app inbox AND the outbox for SMS / e-mail.
      A row is inserted in the same transaction as the change it reports (purchase, lock, PIN change...);
      the API's dispatcher sends it afterwards and sets DeliveryStatus = Sent.
      Never contains secrets: cards appear as "card ending 4057".
   ------------------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Notifications', N'U') IS NULL
CREATE TABLE dbo.Notifications (
    NotificationId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
    CardholderId      INT            NOT NULL,
    CardId            INT            NULL,              -- NULL for account alerts (e.g. a new sign-in)
    Category          NVARCHAR(20)   NOT NULL
                      CONSTRAINT CK_Notifications_Category CHECK (Category IN (N'Transaction', N'Card', N'Security')),
    Title             NVARCHAR(100)  NOT NULL,
    Message           NVARCHAR(500)  NOT NULL,
    CreatedAt         DATETIME2      NOT NULL CONSTRAINT DF_Notifications_CreatedAt DEFAULT SYSUTCDATETIME(),
    ReadAt            DATETIME2      NULL,
    DeliveryStatus    NVARCHAR(20)   NOT NULL CONSTRAINT DF_Notifications_DeliveryStatus DEFAULT N'Pending'
                      CONSTRAINT CK_Notifications_DeliveryStatus CHECK (DeliveryStatus IN (N'Pending', N'Sent', N'Failed')),
    DeliveryAttempts  INT            NOT NULL CONSTRAINT DF_Notifications_DeliveryAttempts DEFAULT 0
                      CONSTRAINT CK_Notifications_DeliveryAttempts CHECK (DeliveryAttempts >= 0),
    SentAt            DATETIME2      NULL,
    CONSTRAINT FK_Notifications_Cardholders FOREIGN KEY (CardholderId) REFERENCES dbo.Cardholders(CardholderId),
    CONSTRAINT FK_Notifications_CreditCards FOREIGN KEY (CardId) REFERENCES dbo.CreditCards(CardId)
);
GO

-- The inbox: "my newest alerts" and the unread badge.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Notifications_Cardholder_Created')
    CREATE INDEX IX_Notifications_Cardholder_Created ON dbo.Notifications(CardholderId, CreatedAt DESC);
GO

-- The dispatcher: "what still has to be sent?" A filtered index stays tiny - only unsent rows are in it.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Notifications_Pending')
    CREATE INDEX IX_Notifications_Pending ON dbo.Notifications(NotificationId) WHERE DeliveryStatus = N'Pending';
GO

/* -------------------------------------------------------------------------------------
   3. SecurityAuditLogs.Outcome gets 'Challenged': the request was answered with HTTP 428
      ("a one-time code was sent, send the request again with it").
      (The append-only trigger blocks UPDATE / DELETE of rows; changing a constraint is allowed.)
   ------------------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_SecurityAuditLogs_Outcome'
             AND definition NOT LIKE N'%Challenged%')
    ALTER TABLE dbo.SecurityAuditLogs DROP CONSTRAINT CK_SecurityAuditLogs_Outcome;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_SecurityAuditLogs_Outcome')
    ALTER TABLE dbo.SecurityAuditLogs ADD CONSTRAINT CK_SecurityAuditLogs_Outcome
        CHECK (Outcome IN (N'Success', N'Rejected', N'Failed', N'Challenged'));
GO
