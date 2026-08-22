package store

import "__GO_MODULE__/server/internal/platform/database"

// The schema history, whole. It is one unit because its value is its order: migration 2 only reads
// correctly underneath migration 1, and splitting a sequence turns "has this shipped?" into a
// question you answer by opening every file.

// Migrations is the module's append-only schema history.
//
// Never edit a migration that has shipped: it is keyed by name, so a released migration whose SQL
// changed will not re-run on a database that already has it, and the schema silently diverges
// between deployments. Add a new one instead.
func Migrations() []database.Migration {
	return []database.Migration{
		{
			Name: "0001_create_plugins",
			SQL: `
                CREATE TABLE plugins.Plugin (
                    Id          bigint         NOT NULL IDENTITY(1,1),
                    PluginId    nvarchar(64)   NOT NULL,
                    DisplayName nvarchar(120)  NOT NULL,
                    Description nvarchar(400)  NOT NULL
                        CONSTRAINT DF_Plugin_Description DEFAULT N'',
                    Publisher   nvarchar(200)  NOT NULL
                        CONSTRAINT DF_Plugin_Publisher DEFAULT N'',
                    IsListed    bit            NOT NULL
                        CONSTRAINT DF_Plugin_IsListed DEFAULT 1,
                    CreatedAt   datetime2(3)   NOT NULL,
                    UpdatedAt   datetime2(3)   NOT NULL,
                    CONSTRAINT PK_PluginId PRIMARY KEY (Id),
                    CONSTRAINT AK_Plugin_PluginId UNIQUE (PluginId)
                );

                /*
                    The artifact lives beside its metadata rather than on a disk the server would
                    have to own, back up and keep in step with the rows describing it. The size is
                    capped in the domain, so a row stays something a single read can hold.
                */
                CREATE TABLE plugins.PluginVersion (
                    Id          bigint         NOT NULL IDENTITY(1,1),
                    PluginId    bigint         NOT NULL,
                    Version     nvarchar(32)   NOT NULL,
                    MinHostSdk  nvarchar(16)   NOT NULL,
                    SizeInBytes bigint         NOT NULL,
                    Sha256      char(64)       NOT NULL,
                    Content     varbinary(max) NOT NULL,
                    IsYanked    bit            NOT NULL
                        CONSTRAINT DF_PluginVersion_IsYanked DEFAULT 0,
                    PublishedAt datetime2(3)   NOT NULL,
                    CONSTRAINT PK_PluginVersionId PRIMARY KEY (Id),
                    CONSTRAINT FK_PluginVersion_PluginPluginId FOREIGN KEY (PluginId)
                        REFERENCES plugins.Plugin (Id) ON DELETE CASCADE,
                    CONSTRAINT AK_PluginVersion_PluginIdVersion UNIQUE (PluginId, Version)
                );

                /*
                    Every read of a plugin's versions is "newest first", so the index is declared
                    in that direction. Id breaks ties: two versions published in the same
                    millisecond would otherwise come back in whatever order the engine felt like.
                */
                CREATE INDEX IX_PluginVersion_PluginId_PublishedAt_Id
                    ON plugins.PluginVersion (PluginId, PublishedAt DESC, Id DESC);

                /*
                    What an account has installed, and where it said the package came from. The
                    source is the difference a reader acts on, so it is a column rather than
                    something inferred from whether the version is one this catalog holds.
                */
                CREATE TABLE plugins.PluginInstall (
                    Id          bigint         NOT NULL IDENTITY(1,1),
                    UserId      nvarchar(64)   NOT NULL,
                    PluginId    nvarchar(64)   NOT NULL,
                    Version     nvarchar(32)   NOT NULL,
                    Source      nvarchar(16)   NOT NULL,
                    InstalledAt datetime2(3)   NOT NULL,
                    CONSTRAINT PK_PluginInstallId PRIMARY KEY (Id)
                );

                CREATE INDEX IX_PluginInstall_UserId_InstalledAt_Id
                    ON plugins.PluginInstall (UserId, InstalledAt DESC, Id DESC);
			`,
		},
	}
}
