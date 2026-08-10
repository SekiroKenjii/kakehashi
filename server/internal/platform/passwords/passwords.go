// Package passwords hashes and verifies user passwords with Argon2id.
//
// Argon2id rather than bcrypt: it is the current recommendation, it resists GPU and side-channel
// attacks better, and it lets the cost be tuned along two axes instead of one. The parameters
// below follow OWASP's baseline.
//
// The encoded form carries its own parameters, so raising the cost later does not invalidate
// existing hashes — old ones keep verifying with the parameters they were made with, and get
// re-hashed the next time their owner signs in successfully. NeedsRehash reports when that is.
package passwords

import (
	"crypto/rand"
	"crypto/subtle"
	"encoding/base64"
	"errors"
	"fmt"
	"strings"

	"golang.org/x/crypto/argon2"
)

// Parameters of the Argon2id computation. They are written into every hash, so changing them here
// affects new passwords only.
const (
	// memoryKiB is the single most important cost knob: it is what makes parallel cracking
	// expensive. 64 MiB is OWASP's floor.
	memoryKiB = 64 * 1024

	// iterations, with memory this high, can stay low.
	iterations = 3

	// parallelism should not exceed the cores a server can spare per sign-in.
	parallelism = 2

	saltLength = 16
	keyLength  = 32
)

// ErrInvalidHash is returned when a stored hash cannot be parsed. It means the column was
// corrupted or written by something else — never that the password was wrong.
var ErrInvalidHash = errors.New("password hash is malformed")

// Hash returns the encoded Argon2id hash of plain.
//
// The result looks like $argon2id$v=19$m=65536,t=3,p=2$<salt>$<key> and is safe to store as-is:
// the salt is unique per password and the parameters travel with it.
func Hash(plain string) (string, error) {
	salt := make([]byte, saltLength)
	if _, err := rand.Read(salt); err != nil {
		return "", fmt.Errorf("generate password salt: %w", err)
	}

	key := argon2.IDKey([]byte(plain), salt, iterations, memoryKiB, parallelism, keyLength)

	return fmt.Sprintf(
		"$argon2id$v=%d$m=%d,t=%d,p=%d$%s$%s",
		argon2.Version, memoryKiB, iterations, parallelism,
		base64.RawStdEncoding.EncodeToString(salt),
		base64.RawStdEncoding.EncodeToString(key),
	), nil
}

// Verify reports whether plain produced encoded.
//
// A malformed hash returns false with an error, and both have to be treated as "not authenticated"
// by the caller. What it must never do is distinguish the two to the user: "that account is
// broken" and "wrong password" are the same answer from outside.
func Verify(encoded, plain string) (bool, error) {
	memory, time, threads, salt, want, err := decode(encoded)
	if err != nil {
		return false, err
	}

	got := argon2.IDKey([]byte(plain), salt, time, memory, threads, uint32(len(want)))

	// Constant time: a byte-by-byte comparison leaks how much of the hash matched, which is enough
	// to reconstruct it one byte at a time given enough attempts.
	return subtle.ConstantTimeCompare(got, want) == 1, nil
}

// NeedsRehash reports whether encoded was produced with weaker parameters than the current ones.
//
// Call it after a successful verification: that is the only moment the plaintext is in hand, and
// therefore the only moment the hash can be upgraded without asking the user for anything.
func NeedsRehash(encoded string) bool {
	memory, time, threads, _, _, err := decode(encoded)
	if err != nil {
		return true
	}
	return memory < memoryKiB || time < iterations || threads < parallelism
}

func decode(encoded string) (
	memory, time uint32, threads uint8, salt, key []byte, err error,
) {
	parts := strings.Split(encoded, "$")
	if len(parts) != 6 || parts[1] != "argon2id" {
		return 0, 0, 0, nil, nil, ErrInvalidHash
	}

	var version int
	if _, err := fmt.Sscanf(parts[2], "v=%d", &version); err != nil || version != argon2.Version {
		return 0, 0, 0, nil, nil, ErrInvalidHash
	}
	if _, err := fmt.Sscanf(parts[3], "m=%d,t=%d,p=%d", &memory, &time, &threads); err != nil {
		return 0, 0, 0, nil, nil, ErrInvalidHash
	}

	if salt, err = base64.RawStdEncoding.Strict().DecodeString(parts[4]); err != nil {
		return 0, 0, 0, nil, nil, ErrInvalidHash
	}
	if key, err = base64.RawStdEncoding.Strict().DecodeString(parts[5]); err != nil {
		return 0, 0, 0, nil, nil, ErrInvalidHash
	}
	if len(key) == 0 {
		return 0, 0, 0, nil, nil, ErrInvalidHash
	}

	return memory, time, threads, salt, key, nil
}
