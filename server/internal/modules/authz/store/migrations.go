package store

import "github.com/SekiroKenjii/kakehashi/server/internal/platform/database"

// The schema history, whole, because its value is its order.

// Migrations is the module's append-only schema history.
//
// Never edit one that has shipped: it is keyed by name, so changed SQL will not re-run on a
// database that already has it and the schema silently diverges. Add another.
func Migrations() []database.Migration {
	return []database.Migration{
		{
			Name: "0001_create_authz",
			SQL: `
                /*
                    The textbook shape: users to roles to permissions, many-to-many at both joins.
                    What is not textbook is the Scope column on the grant — that is the row-level
                    half, and it lives here rather than in a table of its own because a permission
                    and the rows it reaches are one decision. Splitting them is how they drift.

                    ROLE is not reserved in T-SQL; PERMISSION is not either. Both were checked.
                */
                CREATE TABLE authz.Permission (
                    [Key]       nvarchar(64)  NOT NULL,
                    Name        nvarchar(100) NOT NULL,
                    Description nvarchar(400) NOT NULL
                        CONSTRAINT DF_Permission_Description DEFAULT N'',
                    Category    nvarchar(64)  NOT NULL,
                    IsHighRisk  bit           NOT NULL
                        CONSTRAINT DF_Permission_IsHighRisk DEFAULT 0,
                    CONSTRAINT PK_PermissionKey PRIMARY KEY ([Key])
                );

                /*
                    The one place square brackets appear in this schema, and the reason is worth
                    stating: KEY is a reserved word. The column is named Key because that is what
                    the contract calls it on both sides of the wire, and renaming it to PermissionKey
                    here would put a translation in every query for the sake of avoiding one pair of
                    brackets on a column nobody joins on by name.
                */

                CREATE TABLE authz.Role (
                    Id          nvarchar(64)  NOT NULL,
                    Name        nvarchar(64)  NOT NULL,
                    Description nvarchar(200) NOT NULL
                        CONSTRAINT DF_Role_Description DEFAULT N'',
                    IsSystem    bit           NOT NULL
                        CONSTRAINT DF_Role_IsSystem DEFAULT 0,
                    CreatedAt   datetime2(3)  NOT NULL,
                    UpdatedAt   datetime2(3)  NOT NULL,
                    CONSTRAINT PK_RoleId PRIMARY KEY (Id)
                );

                CREATE UNIQUE INDEX AK_Role_Name ON authz.Role (Name);

                /*
                    The grant. The pair is the whole identity of a row, which is what stops one role
                    holding one permission at two scopes — a shape that makes "how far does this
                    reach" unanswerable.
                */
                CREATE TABLE authz.RolePermission (
                    RoleId        nvarchar(64) NOT NULL,
                    PermissionKey nvarchar(64) NOT NULL,
                    Scope         nvarchar(16) NOT NULL
                        CONSTRAINT DF_RolePermission_Scope DEFAULT N'all',
                    GrantedBy     nvarchar(64) NOT NULL,
                    GrantedAt     datetime2(3) NOT NULL,
                    CONSTRAINT PK_RolePermission PRIMARY KEY (RoleId, PermissionKey),
                    CONSTRAINT CK_RolePermission_Scope
                        CHECK (Scope IN (N'own', N'team', N'all')),
                    CONSTRAINT FK_RolePermission_RoleId
                        FOREIGN KEY (RoleId) REFERENCES authz.Role (Id) ON DELETE CASCADE
                );

                /*
                    Deleting a role takes its grants with it, which is the one cascade in this
                    schema and the one place a cascade is right: a grant on a deleted role is
                    unreachable by construction.

                    There is deliberately no foreign key to authz.Permission. The catalogue is
                    reconciled from what the modules declare at boot, so unmounting a module would
                    otherwise delete every grant referencing it — and remounting it would not bring
                    them back. A grant naming a permission nothing declares is inert, which is the
                    behaviour you want while a module is temporarily out of the build.
                */

                CREATE TABLE authz.AccountRole (
                    AccountId  nvarchar(64) NOT NULL,
                    RoleId     nvarchar(64) NOT NULL,
                    AssignedBy nvarchar(64) NOT NULL,
                    AssignedAt datetime2(3) NOT NULL,
                    CONSTRAINT PK_AccountRole PRIMARY KEY (AccountId, RoleId),
                    CONSTRAINT FK_AccountRole_RoleId
                        FOREIGN KEY (RoleId) REFERENCES authz.Role (Id) ON DELETE CASCADE
                );

                /*
                    No foreign key to account.Account: that is another module's schema, and the one
                    place this project's namespacing is a review rule rather than something the
                    engine checks. A cascade there would let deleting an account rewrite this
                    module's rows.

                    The index serves the other direction — "who holds this role" — which is what the
                    role list's user count asks and what an access review asks.
                */
                CREATE INDEX IX_AccountRole_RoleId_AccountId
                    ON authz.AccountRole (RoleId, AccountId);

                /*
                    The audit trail. Append-only, and read newest-first, so the index is declared in
                    that direction with Id breaking ties for the same reason the notes list does.
                */
                CREATE TABLE authz.AuditEntry (
                    Id            nvarchar(64)  NOT NULL,
                    OccurredAt    datetime2(3)  NOT NULL,
                    ActorId       nvarchar(64)  NOT NULL,
                    Action        nvarchar(32)  NOT NULL,
                    RoleId        nvarchar(64)  NOT NULL
                        CONSTRAINT DF_AuditEntry_RoleId DEFAULT N'',
                    PermissionKey nvarchar(64)  NOT NULL
                        CONSTRAINT DF_AuditEntry_PermissionKey DEFAULT N'',
                    Detail        nvarchar(400) NOT NULL
                        CONSTRAINT DF_AuditEntry_Detail DEFAULT N'',
                    CONSTRAINT PK_AuditEntryId PRIMARY KEY (Id)
                );

                CREATE INDEX IX_AuditEntry_OccurredAt_Id
                    ON authz.AuditEntry (OccurredAt DESC, Id DESC);
			`,
		},
		{
			Name: "0002_audit_names",
			SQL: `
                /*
                    The names, copied onto the entry rather than joined at read time.

                    Denormalisation, and deliberate. An audit trail is read months later, when the
                    administrator may have been deleted and the role renamed — a join would then
                    render a blank exactly where the reader is looking, which reads as "nobody did
                    this" rather than "this person did". The trail records what was true at the
                    moment, and a name is part of that.

                    Defaulted to empty so the rows written before this migration stay readable:
                    their Detail column already carries the role name, and a blank is honest about
                    a name that was never captured.
                */
                ALTER TABLE authz.AuditEntry ADD
                    ActorName nvarchar(200) NOT NULL
                        CONSTRAINT DF_AuditEntry_ActorName DEFAULT N'',
                    RoleName  nvarchar(64)  NOT NULL
                        CONSTRAINT DF_AuditEntry_RoleName DEFAULT N'';
			`,
		},
		{
			Name: "0003_permission_is_scoped",
			SQL: `
                /*
                    Whether a permission's row scope is honoured by whatever enforces it. Declared
                    by the module, reconciled at boot like the rest of the catalogue, and read by
                    the administration screen so the own/team/all picker appears only where
                    choosing changes an answer.
                */
                IF COL_LENGTH('authz.Permission', 'IsScoped') IS NULL
                    ALTER TABLE authz.Permission
                        ADD IsScoped bit NOT NULL
                            CONSTRAINT DF_Permission_IsScoped DEFAULT 0;
			`,
		},
		{
			Name: "0004_permission_key_rename",
			SQL: `
                /*
                    [Key] was the only bracketed identifier in this schema, and the comment beside it
                    argued the brackets were worth it because the wire calls the field "key". They
                    are not: the wire keeps its name either way, and a reserved word in a column name
                    is a footgun every future query has to remember. PermissionKey also matches what
                    the referencing table already calls it.
                */
                IF COL_LENGTH('authz.Permission', 'PermissionKey') IS NULL
                    EXEC sp_rename 'authz.Permission.[Key]', 'PermissionKey', 'COLUMN';
			`,
		},
	}
}
