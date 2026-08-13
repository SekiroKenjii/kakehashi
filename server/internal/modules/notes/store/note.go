package store

import (
	"context"
	"database/sql"
	"errors"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/notes/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Every query against notes.Note. One file per table is the store package's unit, and this module
// has one table.

// List returns every note, most recently updated first.
func (s *SQLServer) List(ctx context.Context) ([]domain.Note, error) {
	const q = `
        SELECT n.Id, n.Title, n.Body, n.CreatedAt, n.UpdatedAt
        FROM notes.Note AS n
        ORDER BY n.UpdatedAt DESC, n.Id DESC;`

	rows, err := s.db.QueryContext(ctx, q)
	if err != nil {
		return nil, errs.Internalf(err, "list notes")
	}
	defer rows.Close()

	var notes []domain.Note
	for rows.Next() {
		n, err := scanNote(rows)
		if err != nil {
			return nil, err
		}
		notes = append(notes, n)
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "list notes")
	}
	return notes, nil
}

// Get returns the note with the given ID.
func (s *SQLServer) Get(ctx context.Context, id int64) (domain.Note, error) {
	const q = `
        SELECT n.Id, n.Title, n.Body, n.CreatedAt, n.UpdatedAt
        FROM notes.Note AS n
        WHERE n.Id = @p1;`

	n, err := scanNote(s.db.QueryRowContext(ctx, q, id))
	if errors.Is(err, sql.ErrNoRows) {
		return domain.Note{}, errs.NotFoundf("No note with ID %d.", id)
	}
	if err != nil {
		return domain.Note{}, err
	}
	return n, nil
}

// Insert stores n and returns it with its assigned ID.
func (s *SQLServer) Insert(ctx context.Context, n domain.Note) (domain.Note, error) {
	// OUTPUT INSERTED.id, not a follow-up SELECT: go-mssqldb has no LastInsertId, and OUTPUT stays
	// correct under concurrent inserts and triggers where SCOPE_IDENTITY() only mostly does.
	const q = `
        INSERT INTO notes.Note (Title, Body, CreatedAt, UpdatedAt)
        OUTPUT INSERTED.Id
        VALUES (@p1, @p2, @p3, @p4);`

	// Truncated to the column's precision and returned that way: otherwise a create answers in
	// nanoseconds, the next read in milliseconds, and the timestamp appears to change by itself.
	n.CreatedAt = storable(n.CreatedAt)
	n.UpdatedAt = storable(n.UpdatedAt)

	var id int64
	err := s.db.
		QueryRowContext(ctx, q, n.Title, n.Body, n.CreatedAt, n.UpdatedAt).
		Scan(&id)
	if err != nil {
		return domain.Note{}, errs.Internalf(err, "insert note")
	}

	n.ID = id
	return n, nil
}

// Update rewrites an existing note.
//
// It takes a pointer so the truncated UpdatedAt goes back to the caller, for the same reason
// Insert returns the note it stored.
func (s *SQLServer) Update(ctx context.Context, n *domain.Note) error {
	const q = `
        UPDATE notes.Note
        SET Title = @p1, Body = @p2, UpdatedAt = @p3
        WHERE Id = @p4;`

	n.UpdatedAt = storable(n.UpdatedAt)

	res, err := s.db.ExecContext(ctx, q, n.Title, n.Body, n.UpdatedAt, n.ID)
	if err != nil {
		return errs.Internalf(err, "update note")
	}

	affected, err := res.RowsAffected()
	if err != nil {
		return errs.Internalf(err, "update note")
	}
	if affected == 0 {
		return errs.NotFoundf("No note with ID %d.", n.ID)
	}
	return nil
}

// Delete removes a note. Deleting one that does not exist is not an error: the caller asked for it
// to be gone, and afterwards it is.
func (s *SQLServer) Delete(ctx context.Context, id int64) error {
	const q = `DELETE FROM notes.Note WHERE Id = @p1;`

	if _, err := s.db.ExecContext(ctx, q, id); err != nil {
		return errs.Internalf(err, "delete note")
	}
	return nil
}
