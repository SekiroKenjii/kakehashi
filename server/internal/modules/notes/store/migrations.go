package store

import "github.com/SekiroKenjii/kakehashi/server/internal/platform/database"

// Migrations is the module's append-only schema history, kept in one file because its value is its
// order.
//
// Never edit a migration that has shipped: it is keyed by name, so a released migration whose SQL
// changed will not re-run on a database that already has it, and the schema silently diverges
// between deployments. Add a new one instead.
func Migrations() []database.Migration {
	return []database.Migration{
		{
			Name: "0001_create_notes",
			SQL: `
                CREATE TABLE notes.Note (
                    Id        bigint         NOT NULL IDENTITY(1,1),
                    Title     nvarchar(120)  NOT NULL,
                    Body      nvarchar(max)  NOT NULL
                        CONSTRAINT DF_Note_Body DEFAULT N'',
                    CreatedAt datetime2(3)   NOT NULL,
                    UpdatedAt datetime2(3)   NOT NULL,
                    CONSTRAINT PK_NoteId PRIMARY KEY (Id)
                );

                /*
                    The list query is "newest first", so the index is declared in that direction.
                    Id breaks ties: two notes saved in the same millisecond would otherwise come
                    back in whatever order the engine felt like, and a list that reshuffles itself
                    between refreshes looks broken even when the data is right.
                */
                CREATE INDEX IX_Note_UpdatedAt_Id
                    ON notes.Note (UpdatedAt DESC, Id DESC);
			`,
		},
	}
}
