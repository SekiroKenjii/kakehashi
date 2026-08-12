package store

import "github.com/SekiroKenjii/kakehashi/server/internal/platform/database"

// Migrations is the module's append-only schema history, kept in one file because its value is its
// order.
//
// Never edit one that has shipped: it is keyed by name, so changed SQL will not re-run on a database
// that already has it and the schema silently diverges. Add another.
//
// The corollary, learned the hard way: RENAMING one is editing it. The ledger still holds the old
// name, so the migration runs a second time against a database that already has its effect. Every
// migration after the first is therefore written to be safe to re-apply — a guard costs two lines
// and removes a whole class of "it worked on my machine".
func Migrations() []database.Migration {
	return []database.Migration{
		{
			Name: "0001_create_navigation",
			SQL: `
                /*
                    Two tables, and between them they hold exactly one thing: where the pane's
                    destinations sit. What destinations EXIST is not here and cannot be — that is
                    declared in code, because a destination is a compiled page behind a permission
                    and no row can create one. This is the presentation half, and it is in a database
                    so that rearranging it is an afternoon rather than a release.
                */
                CREATE TABLE navigation.NavGroup (
                    Id        nvarchar(64) NOT NULL,
                    Title     nvarchar(64) NOT NULL,
                    SortOrder int          NOT NULL
                        CONSTRAINT DF_NavGroup_SortOrder DEFAULT 0,
                    IsSystem  bit          NOT NULL
                        CONSTRAINT DF_NavGroup_IsSystem DEFAULT 0,
                    CreatedAt datetime2(3) NOT NULL,
                    UpdatedAt datetime2(3) NOT NULL,
                    CONSTRAINT PK_NavGroupId PRIMARY KEY (Id)
                );

                /*
                    Titles are unique because two headings reading "Administration" is not a
                    configuration anyone meant, and the pane gives no way to tell which is which.
                */
                CREATE UNIQUE INDEX AK_NavGroup_Title ON navigation.NavGroup (Title);

                CREATE TABLE navigation.NavItem (
                    Id        nvarchar(96) NOT NULL,
                    ModuleId  nvarchar(64) NOT NULL,

                    /*
                        Nullable, and ON DELETE SET NULL rather than CASCADE. Deleting a heading is
                        a decision about the heading; the destinations under it are still compiled
                        into the client and still have to go somewhere. They fall to ungrouped and
                        wait to be filed again, which loses an administrator one placement instead
                        of a page.
                    */
                    GroupId   nvarchar(64) NULL,

                    /*
                        Overrides, both nullable, and NULL is not the same as empty here: NULL means
                        "whatever the code calls it", so a page that gets renamed carries its new
                        name everywhere nobody deliberately renamed it.
                    */
                    Title     nvarchar(64) NULL,
                    Icon      nvarchar(64) NULL,

                    SortOrder int          NOT NULL
                        CONSTRAINT DF_NavItem_SortOrder DEFAULT 0,
                    IsVisible bit          NOT NULL
                        CONSTRAINT DF_NavItem_IsVisible DEFAULT 1,
                    UpdatedAt datetime2(3) NOT NULL,
                    CONSTRAINT PK_NavItemId PRIMARY KEY (Id),
                    CONSTRAINT FK_NavItem_NavGroup FOREIGN KEY (GroupId)
                        REFERENCES navigation.NavGroup (Id) ON DELETE SET NULL
                );

                /*
                    The read is "every placement, grouped and in order", which this index answers
                    end to end. Id breaks ties: two destinations sharing a sort order would
                    otherwise come back in whatever order the engine felt like, and a pane that
                    reshuffles itself between sign-ins looks broken even when the data is right.
                */
                CREATE INDEX IX_NavItem_GroupId_SortOrder
                    ON navigation.NavItem (GroupId, SortOrder, Id);
			`,
		},
		{
			Name: "0002_navitem_order_tiebreak",
			SQL: `
                /*
                    The index the first migration meant to create. Its comment says Id breaks ties
                    so a pane does not reshuffle itself between refreshes — and then the index was
                    written (GroupId, SortOrder, Id), which orders by GroupId first and so cannot
                    serve the ORDER BY the reads actually issue (SortOrder, Id).
                */
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = 'IX_NavItem_GroupId_SortOrder'
                      AND object_id = OBJECT_ID('navigation.NavItem')
                )
                    DROP INDEX IX_NavItem_GroupId_SortOrder ON navigation.NavItem;

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = 'IX_NavItem_SortOrder_Id'
                      AND object_id = OBJECT_ID('navigation.NavItem')
                )
                    CREATE INDEX IX_NavItem_SortOrder_Id
                        ON navigation.NavItem (SortOrder, Id)
                        INCLUDE (GroupId, ModuleId, Title, Icon, IsVisible);
			`,
		},
		{
			Name: "0003_navitem_fk_name",
			SQL: `
                /*
                    sqlserver-kit names a foreign key for the column it constrains, not just the
                    table it points at — FK_NavItem_NavGroupId rather than FK_NavItem_NavGroup. It
                    matters the day NavItem grows a second reference to the same table.
                */
                IF EXISTS (
                    SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_NavItem_NavGroup'
                )
                    EXEC sp_rename 'navigation.FK_NavItem_NavGroup', 'FK_NavItem_NavGroupId', 'OBJECT';
			`,
		},
	}
}
