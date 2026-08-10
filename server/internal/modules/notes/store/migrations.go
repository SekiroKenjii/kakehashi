package store

import "github.com/SekiroKenjii/kakehashi/server/internal/platform/database"

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
