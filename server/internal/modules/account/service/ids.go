package service

import "github.com/google/uuid"

// Version 4, so identifiers carry no timestamp and no MAC address. Session and event ids end up in
// logs and, in the session case, on a page the user can read; an identifier that leaks when it was
// made and on what hardware is an identifier that answers questions nobody asked it.
func newUUID() string {
	return uuid.NewString()
}
