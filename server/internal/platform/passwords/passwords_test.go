package passwords

import (
	"errors"
	"strings"
	"testing"
)

const secret = "correct horse battery staple"

func TestHashProducesAVerifiableEncoding(t *testing.T) {
	encoded, err := Hash(secret)
	if err != nil {
		t.Fatalf("Hash returned an error: %v", err)
	}

	if !strings.HasPrefix(encoded, "$argon2id$") {
		t.Errorf("encoding = %q, want it to start with $argon2id$", encoded)
	}
	// The parameters travel with the hash; without them an old hash could not be verified after
	// the cost is raised.
	if !strings.Contains(encoded, "m=65536,t=3,p=2") {
		t.Errorf("encoding = %q, want it to carry its parameters", encoded)
	}

	ok, err := Verify(encoded, secret)
	if err != nil {
		t.Fatalf("Verify returned an error: %v", err)
	}
	if !ok {
		t.Error("the password did not verify against its own hash")
	}
}

func TestHashIsSaltedPerPassword(t *testing.T) {
	first, err := Hash(secret)
	if err != nil {
		t.Fatalf("Hash returned an error: %v", err)
	}
	second, err := Hash(secret)
	if err != nil {
		t.Fatalf("Hash returned an error: %v", err)
	}

	// Equal hashes would mean an unsalted scheme, where one rainbow table breaks every account
	// sharing a password.
	if first == second {
		t.Error("hashing the same password twice produced the same encoding; the salt is not random")
	}
}

func TestVerifyRejectsTheWrongPassword(t *testing.T) {
	encoded, err := Hash(secret)
	if err != nil {
		t.Fatalf("Hash returned an error: %v", err)
	}

	for _, wrong := range []string{"", "Correct horse battery staple", secret + " "} {
		ok, err := Verify(encoded, wrong)
		if err != nil {
			t.Fatalf("Verify(%q) returned an error: %v", wrong, err)
		}
		if ok {
			t.Errorf("Verify(%q) accepted the wrong password", wrong)
		}
	}
}

func TestVerifyReportsAMalformedHash(t *testing.T) {
	// A corrupt column must be an error, not a silent false: only one of the two is the user's
	// fault.
	malformed := []string{
		"",
		"not-a-hash",
		"$argon2i$v=19$m=65536,t=3,p=2$c2FsdA$aGFzaA", // wrong variant
		"$argon2id$v=1$m=65536,t=3,p=2$c2FsdA$aGFzaA", // wrong version
		"$argon2id$v=19$m=65536,t=3$c2FsdA$aGFzaA",    // missing a parameter
		"$argon2id$v=19$m=65536,t=3,p=2$!!!$aGFzaA",   // salt is not base64
		"$argon2id$v=19$m=65536,t=3,p=2$c2FsdA$",      // no key
	}

	for _, encoded := range malformed {
		ok, err := Verify(encoded, secret)
		if ok {
			t.Errorf("Verify(%q) reported a match", encoded)
		}
		if !errors.Is(err, ErrInvalidHash) {
			t.Errorf("Verify(%q) error = %v, want ErrInvalidHash", encoded, err)
		}
	}
}

func TestNeedsRehash(t *testing.T) {
	current, err := Hash(secret)
	if err != nil {
		t.Fatalf("Hash returned an error: %v", err)
	}
	if NeedsRehash(current) {
		t.Error("a hash made with the current parameters wants rehashing")
	}

	// Half the memory: exactly the case the upgrade path exists for.
	weaker := "$argon2id$v=19$m=32768,t=3,p=2$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYQ"
	if !NeedsRehash(weaker) {
		t.Error("a hash made with weaker parameters does not want rehashing")
	}

	// Unparseable counts as needing a rehash: whatever it is, it is not a current hash.
	if !NeedsRehash("garbage") {
		t.Error("an unparseable hash does not want rehashing")
	}
}
