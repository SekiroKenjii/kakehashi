package domain

import "time"

// SigningKey is the provider's token-signing key, held as a PKCS#8 PEM.
//
// An aggregate root of one: it has no children, and its lifecycle — generated once, later
// superseded by a newer one — belongs to nothing else. Rotation is inserting a row, not editing
// this one, which is why there is no method here that changes a key.
//
// The private half never leaves the module. Parsing it into a usable key is the wire layer's job,
// because that is where the crypto library lives and this package imports none.
type SigningKey struct {
	ID string

	// Algorithm is the JOSE signature algorithm, e.g. RS256.
	Algorithm string

	// PrivateKey is PEM-encoded PKCS#8. It is in the database rather than a file so every replica
	// signs with the same key, and so a redeploy does not invalidate every token in the field.
	PrivateKey string

	CreatedAt time.Time
}
