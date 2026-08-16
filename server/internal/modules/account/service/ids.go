package service

import "github.com/google/uuid"

// newUUID is the production ID generator.
//
// Version 4, so identifiers carry no timestamp and no MAC address: session and event ids end up
// in logs and, in the session case, on a page the user can read.
func newUUID() string {
	return uuid.NewString()
}
