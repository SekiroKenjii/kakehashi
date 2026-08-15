package store

import (
	"__GO_MODULE__/server/internal/platform/database"
)

// Migrations is the module's append-only schema history. Never edit one that has shipped.
func Migrations() []database.Migration {
	return []database.Migration{
		{
			Name: "0001_create_account",
			SQL: `
                CREATE TABLE account.Account (
                    Id           nvarchar(64)  NOT NULL,
                    Email        nvarchar(254) NOT NULL,
                    DisplayName  nvarchar(100) NOT NULL,
                    Phone        nvarchar(32)  NOT NULL
                        CONSTRAINT DF_Account_Phone DEFAULT N'',
                    PasswordHash nvarchar(256) NOT NULL,
                    Roles        nvarchar(400) NOT NULL
                        CONSTRAINT DF_Account_Roles DEFAULT N'',
                    CreatedAt    datetime2(3)  NOT NULL,
                    UpdatedAt    datetime2(3)  NOT NULL,
                    CONSTRAINT PK_AccountId PRIMARY KEY (Id)
                );

                /*
                    The address is the account's identity, so the database says so. Relying on a
                    SELECT-before-INSERT instead would let two concurrent signups both succeed.
                */
                CREATE UNIQUE INDEX AK_Account_Email ON account.Account (Email);

                CREATE TABLE account.AuthRequest (
                    Id                  nvarchar(64)  NOT NULL,
                    ClientId            nvarchar(128) NOT NULL,
                    Subject             nvarchar(64)  NOT NULL
                        CONSTRAINT DF_AuthRequest_Subject DEFAULT N'',
                    Scopes              nvarchar(400) NOT NULL,
                    RedirectUri         nvarchar(512) NOT NULL,
                    ResponseType        nvarchar(32)  NOT NULL,
                    Nonce               nvarchar(128) NOT NULL
                        CONSTRAINT DF_AuthRequest_Nonce DEFAULT N'',
                    AuthState           nvarchar(512) NOT NULL
                        CONSTRAINT DF_AuthRequest_AuthState DEFAULT N'',
                    CodeChallenge       nvarchar(256) NOT NULL
                        CONSTRAINT DF_AuthRequest_CodeChallenge DEFAULT N'',
                    CodeChallengeMethod nvarchar(16)  NOT NULL
                        CONSTRAINT DF_AuthRequest_CodeChallengeMethod DEFAULT N'',
                    AuthCode            nvarchar(450) NULL,   /*
                        op hands back an encrypted JWE, not a bare id: ~250 chars. 450 is the
                        ceiling for a unique index (900 bytes), which the code column needs.
                    */
                    SessionId           nvarchar(64)  NOT NULL
                        CONSTRAINT DF_AuthRequest_SessionId DEFAULT N'',
                    IsDone              bit           NOT NULL
                        CONSTRAINT DF_AuthRequest_IsDone DEFAULT 0,
                    AuthTime            datetime2(3)  NULL,
                    CreatedAt           datetime2(3)  NOT NULL,
                    CONSTRAINT PK_AuthRequestId PRIMARY KEY (Id)
                );

                /*
                    Filtered: most rows have no code yet, and a plain unique index would collide on
                    NULL in every engine that treats NULLs as equal for uniqueness. SQL Server is
                    one of them.
                */
                CREATE UNIQUE INDEX AK_AuthRequest_AuthCode
                    ON account.AuthRequest (AuthCode) WHERE AuthCode IS NOT NULL;

                CREATE TABLE account.UserSession (
                    Id         nvarchar(64)  NOT NULL,
                    AccountId  nvarchar(64)  NOT NULL,
                    ClientId   nvarchar(128) NOT NULL,
                    Device     nvarchar(256) NOT NULL
                        CONSTRAINT DF_UserSession_Device DEFAULT N'',
                    IpAddress  nvarchar(64)  NOT NULL
                        CONSTRAINT DF_UserSession_IpAddress DEFAULT N'',
                    CreatedAt  datetime2(3)  NOT NULL,
                    LastSeenAt datetime2(3)  NOT NULL,
                    CONSTRAINT PK_UserSessionId PRIMARY KEY (Id),
                    CONSTRAINT FK_UserSession_AccountId FOREIGN KEY (AccountId)
                        REFERENCES account.Account (Id) ON DELETE CASCADE
                );

                CREATE INDEX IX_UserSession_AccountId_LastSeenAt
                    ON account.UserSession (AccountId, LastSeenAt DESC);

                CREATE TABLE account.IssuedToken (
                    Id           nvarchar(64)  NOT NULL,
                    SessionId    nvarchar(64)  NOT NULL,
                    AccountId    nvarchar(64)  NOT NULL,
                    ClientId     nvarchar(128) NOT NULL,
                    RefreshToken nvarchar(450) NULL,
                    Scopes       nvarchar(400) NOT NULL,
                    Audience     nvarchar(400) NOT NULL
                        CONSTRAINT DF_IssuedToken_Audience DEFAULT N'',
                    AuthTime     datetime2(3)  NOT NULL,
                    ExpiresAt    datetime2(3)  NOT NULL,
                    CreatedAt    datetime2(3)  NOT NULL,
                    CONSTRAINT PK_IssuedTokenId PRIMARY KEY (Id),
                    /*
                        Revoking a session must take its tokens with it, and the database is the
                        only place that can guarantee it happens even when the delete comes from
                        elsewhere.
                    */
                    CONSTRAINT FK_IssuedToken_UserSessionId FOREIGN KEY (SessionId)
                        REFERENCES account.UserSession (Id) ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX AK_IssuedToken_RefreshToken
                    ON account.IssuedToken (RefreshToken) WHERE RefreshToken IS NOT NULL;

                CREATE TABLE account.SecurityEvent (
                    Id         nvarchar(64)  NOT NULL,
                    AccountId  nvarchar(64)  NOT NULL,
                    Kind       nvarchar(64)  NOT NULL,
                    Device     nvarchar(256) NOT NULL
                        CONSTRAINT DF_SecurityEvent_Device DEFAULT N'',
                    IpAddress  nvarchar(64)  NOT NULL
                        CONSTRAINT DF_SecurityEvent_IpAddress DEFAULT N'',
                    OccurredAt datetime2(3)  NOT NULL,
                    CONSTRAINT PK_SecurityEventId PRIMARY KEY (Id)
                );

                /*
                    No foreign key on purpose: the audit trail outlives the account it describes.
                    "Who deleted this user" is exactly the question you cannot answer if the answer
                    was cascade-deleted along with them.
                */
                CREATE INDEX IX_SecurityEvent_AccountId_OccurredAt
                    ON account.SecurityEvent (AccountId, OccurredAt DESC);

                CREATE TABLE account.SigningKey (
                    Id         nvarchar(64)  NOT NULL,
                    Algorithm  nvarchar(16)  NOT NULL,
                    PrivateKey nvarchar(max) NOT NULL,
                    CreatedAt  datetime2(3)  NOT NULL,
                    CONSTRAINT PK_SigningKeyId PRIMARY KEY (Id)
                );
            `,
		},
		{
			Name: "0002_roles_move_to_authz",
			SQL: `
                /*
                    Roles move to the authz module, which owns them as rows. Here they are a
                    space-joined string with no role entity and no permission catalogue. Dropped
                    rather than kept in step: the column's only writer is a development seed, so
                    nothing is worth migrating across.
                */
                ALTER TABLE account.Account
                    DROP CONSTRAINT DF_Account_Roles;

                ALTER TABLE account.Account
                    DROP COLUMN Roles;

                /*
                    TeamId is what the 'team' row scope means. Nullable, because an account outside
                    every team is the ordinary case in a fresh deployment, and a scope that resolves
                    to no rows is the safe reading of it.

                    One nullable column rather than a hierarchy: the seam a product redefines as
                    department, tenant or region, and easier to widen than to unpick.
                */
                ALTER TABLE account.Account
                    ADD TeamId nvarchar(64) NULL;
            `,
		},
		{
			Name: "0003_account_status",
			SQL: `
                /*
                    Two columns the administration screen reads.

                    LastSignInAt is nullable because "never signed in" is a real state and the
                    alternatives mislead: the epoch reads as 1970, and CreatedAt reads as an
                    account in use. A NULL is the one value a screen can render as "Never"
                    without having to guess.

                    IsActive is the switch an administrator throws instead of deleting. Deleting an
                    account takes its audit trail and its sessions with it; deactivating stops the
                    sign-in and leaves the history intact. Defaulted to 1 so every existing row
                    keeps its current behaviour.
                */
                ALTER TABLE account.Account ADD
                    LastSignInAt datetime2(3) NULL,
                    IsActive     bit          NOT NULL
                        CONSTRAINT DF_Account_IsActive DEFAULT 1;
            `,
		},
		{
			Name: "0004_authrequest_expires",
			SQL: `
                /*
                    ExpiresAt puts a clock on in-flight authorizations: without one, an abandoned
                    sign-in stays in the table and its authorization code stays redeemable
                    indefinitely.

                    Existing rows get an expiry in the past: anything already here is from a sign-in
                    nobody completed, and treating it as live would be the bug this fixes.
                */
                IF COL_LENGTH('account.AuthRequest', 'ExpiresAt') IS NULL
                    ALTER TABLE account.AuthRequest
                        ADD ExpiresAt datetime2(3) NOT NULL
                            CONSTRAINT DF_AuthRequest_ExpiresAt DEFAULT '2000-01-01T00:00:00.000';

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = 'IX_AuthRequest_ExpiresAt'
                      AND object_id = OBJECT_ID('account.AuthRequest')
                )
                    CREATE INDEX IX_AuthRequest_ExpiresAt
                        ON account.AuthRequest (ExpiresAt);
			`,
		},
	}
}
