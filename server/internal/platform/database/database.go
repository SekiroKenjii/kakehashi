// Package database wraps the server's SQL Server store. Every module shares one database and one
// pool, and each owns a schema named after its module ID, created by Migrate before the module's
// first migration runs. Inside a module, only store/ may import this package (tools/archlint).
//
// Two driver facts: parameters are @p1, @p2 — the driver does not rewrite ?; and LastInsertId is
// not implemented — use an OUTPUT clause, which is safe under triggers and concurrent inserts:
//
//	INSERT INTO notes.Note (Title, Body) OUTPUT INSERTED.Id VALUES (@p1, @p2);
//
// SQL style follows ktaranov/sqlserver-kit; see docs/ARCHITECTURE.md.
package database

import (
	"context"
	"database/sql"
	"fmt"
	"time"

	_ "github.com/microsoft/go-mssqldb" // registers the "sqlserver" driver
)

// DB is the server's SQL Server handle.
type DB struct {
	*sql.DB
}

// Options configures the pool.
type Options struct {
	DSN          string
	MaxOpenConns int
}

// Migration is one forward schema change owned by a module.
type Migration struct {
	// Name has to be unique within the module and must never change once released. It is the
	// primary key the server uses to decide what has already been applied.
	Name string

	// SQL is one or more statements, run as a single T-SQL batch.
	//
	// A few statements (CREATE VIEW, CREATE PROCEDURE, CREATE TRIGGER) must be alone in their
	// batch. Give those a migration of their own rather than fighting the rule. CREATE SCHEMA is
	// in that list too, which is why Migrate does it for you before the batch runs.
	SQL string
}

// Open connects to SQL Server and prepares the migration bookkeeping.
func Open(ctx context.Context, opts Options) (*DB, error) {
	sqlDB, err := sql.Open("sqlserver", opts.DSN)
	if err != nil {
		return nil, fmt.Errorf("open sql server: %w", err)
	}

	if opts.MaxOpenConns > 0 {
		sqlDB.SetMaxOpenConns(opts.MaxOpenConns)
		// Idle capacity matched to the ceiling. Leaving it at Go's default of 2 means a server
		// under steady load spends its time dialling TLS handshakes it is about to throw away.
		sqlDB.SetMaxIdleConns(opts.MaxOpenConns)
	}
	// Well inside the window where a load balancer or SQL Server drops an idle connection: one
	// killed underneath surfaces as a failed query against a healthy server.
	sqlDB.SetConnMaxLifetime(30 * time.Minute)
	sqlDB.SetConnMaxIdleTime(5 * time.Minute)

	if err := sqlDB.PingContext(ctx); err != nil {
		sqlDB.Close()
		return nil, fmt.Errorf("ping sql server: %w", err)
	}

	db := &DB{DB: sqlDB}
	if err := db.initMigrationTable(ctx); err != nil {
		sqlDB.Close()
		return nil, err
	}
	return db, nil
}

func (db *DB) initMigrationTable(ctx context.Context) error {
	/*
		T-SQL has no CREATE TABLE IF NOT EXISTS, so the existence check is explicit.
	*/
	const stmt = `
        IF OBJECT_ID(N'dbo.SchemaMigration', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.SchemaMigration (
                ModuleName    nvarchar(64)  NOT NULL,
                MigrationName nvarchar(128) NOT NULL,
                AppliedAt     datetime2(3)  NOT NULL
                    CONSTRAINT DF_SchemaMigration_AppliedAt DEFAULT SYSUTCDATETIME(),
                CONSTRAINT PK_SchemaMigration PRIMARY KEY (ModuleName, MigrationName)
            );
        END;`

	if _, err := db.ExecContext(ctx, stmt); err != nil {
		return fmt.Errorf("create dbo.SchemaMigration: %w", err)
	}
	return nil
}

// ensureSchema creates the schema a module owns, if it is not there yet.
//
// One schema per module, named after the module ID, is how table namespacing is expressed: it
// survives a table being renamed, and a module's credentials can be granted rights on its own
// schema and nothing else.
//
// CREATE SCHEMA has to be the only statement in its batch, which is why it is executed on its own
// rather than folded into the migration.
func (db *DB) ensureSchema(ctx context.Context, name string) error {
	/*
	   EXEC only accepts a variable or a literal, not an expression, so the statement is built
	   into a variable first. QUOTENAME is what makes concatenating an identifier safe.
	*/
	const stmt = `
        IF NOT EXISTS (SELECT 1 FROM sys.schemas AS s WHERE s.name = @p1)
        BEGIN
            DECLARE @CreateSchemaSql nvarchar(200) = N'CREATE SCHEMA ' + QUOTENAME(@p1) + N';';
            EXEC(@CreateSchemaSql);
        END;`

	if _, err := db.ExecContext(ctx, stmt, name); err != nil {
		return fmt.Errorf("create schema %s: %w", name, err)
	}
	return nil
}

// Migrate applies the migrations a module has not run yet, in order.
//
// Each migration commits in its own transaction. That is deliberate: if the third of five fails,
// the first two stay applied and recorded, and the next boot resumes at the third instead of
// replaying work that already succeeded.
//
// Migrations are keyed by (module, name), so two modules are free to use the same migration name,
// and renaming a shipped migration makes it run a second time. That is why Migration.Name says not
// to.
func (db *DB) Migrate(ctx context.Context, module string, migrations []Migration) error {
	if err := db.ensureSchema(ctx, module); err != nil {
		return err
	}

	// One migrator at a time across processes: two instances starting together would both see the
	// same gap and create the same object. The lock releases if the process dies holding it.
	conn, err := db.Conn(ctx)
	if err != nil {
		return fmt.Errorf("migrate %s: %w", module, err)
	}
	defer func() { _ = conn.Close() }()

	release, err := acquireMigrationLock(ctx, conn, module)
	if err != nil {
		return err
	}
	defer release()

	applied, err := db.appliedMigrations(ctx, module)
	if err != nil {
		return err
	}

	for _, m := range migrations {
		if _, done := applied[m.Name]; done {
			continue
		}

		tx, err := db.BeginTx(ctx, nil)
		if err != nil {
			return fmt.Errorf("begin migration %s/%s: %w", module, m.Name, err)
		}

		if _, err := tx.ExecContext(ctx, m.SQL); err != nil {
			tx.Rollback()
			return fmt.Errorf("apply migration %s/%s: %w", module, m.Name, err)
		}

		const record = `
            INSERT INTO dbo.SchemaMigration (ModuleName, MigrationName)
            VALUES (@p1, @p2);`
		if _, err := tx.ExecContext(ctx, record, module, m.Name); err != nil {
			tx.Rollback()
			return fmt.Errorf("record migration %s/%s: %w", module, m.Name, err)
		}

		if err := tx.Commit(); err != nil {
			return fmt.Errorf("commit migration %s/%s: %w", module, m.Name, err)
		}
	}

	return nil
}

func (db *DB) appliedMigrations(ctx context.Context, module string) (map[string]struct{}, error) {
	const q = `
        SELECT sm.MigrationName
        FROM dbo.SchemaMigration AS sm
        WHERE sm.ModuleName = @p1;`

	rows, err := db.QueryContext(ctx, q, module)
	if err != nil {
		return nil, fmt.Errorf("read applied migrations: %w", err)
	}
	defer rows.Close()

	applied := make(map[string]struct{})
	for rows.Next() {
		var name string
		if err := rows.Scan(&name); err != nil {
			return nil, fmt.Errorf("scan applied migration: %w", err)
		}
		applied[name] = struct{}{}
	}
	return applied, rows.Err()
}

// acquireMigrationLock serialises migration across processes, returning the release.
//
// Scoped per module, so two modules migrate concurrently and only the same module's migrations
// queue. The timeout is generous because the thing being waited for is another instance's schema
// change, and the failure it prevents — two servers applying the same DDL — costs more than a slow
// boot.
func acquireMigrationLock(
	ctx context.Context, conn *sql.Conn, module string,
) (func(), error) {
	const acquire = `
        DECLARE @result int;
        EXEC @result = sp_getapplock
            @Resource = @p1, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 60000;
        SELECT @result;`

	name := "kakehashi-migrate-" + module

	var result int
	if err := conn.QueryRowContext(ctx, acquire, name).Scan(&result); err != nil {
		return nil, fmt.Errorf("lock migrations for %s: %w", module, err)
	}
	if result < 0 {
		return nil, fmt.Errorf(
			"lock migrations for %s: another instance is migrating (sp_getapplock returned %d)",
			module, result)
	}

	return func() {
		// Best effort, on a context detached from the caller's: a cancelled boot must still let go
		// of the lock, and the session ending releases it anyway.
		releaseCtx, cancel := context.WithTimeout(context.WithoutCancel(ctx), 5*time.Second)
		defer cancel()

		_, _ = conn.ExecContext(releaseCtx,
			`EXEC sp_releaseapplock @Resource = @p1, @LockOwner = 'Session';`, name)
	}, nil
}
