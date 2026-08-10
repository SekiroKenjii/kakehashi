package service

import "github.com/google/uuid"

// newUUID is the production id source. Split out so New's signature stays about what varies.
func newUUID() string { return uuid.NewString() }
