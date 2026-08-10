package domain

import "time"

// The actions an audit entry can describe. Free strings rather than an enum, for the reason the
// account module's security-event kinds are: they cross the wire, an unrecognised one is printable
// and greppable, and every deployment that adds an action would otherwise have to widen a type.
const (
	ActionGranted        = "Granted"
	ActionRevoked        = "Revoked"
	ActionRescoped       = "Rescoped"
	ActionRoleCreated    = "RoleCreated"
	ActionRoleEdited     = "RoleEdited"
	ActionRoleDeleted    = "RoleDeleted"
	ActionRoleAssigned   = "RoleAssigned"
	ActionRoleUnassigned = "RoleUnassigned"
)

// AuditEntry is one recorded change to who may do what.
//
// Not an aggregate root: written once, never modified, and nothing else has to change with it. The
// account module's domain/doc.go makes the same ruling about its SecurityEvent for the same reasons.
//
// It records the CHANGE rather than the resulting state. "Granted devops.sql at scope all" answers
// the question an access review actually asks — who decided this, and when — which a snapshot of
// the end state cannot.
type AuditEntry struct {
	ID         string
	OccurredAt time.Time

	// ActorID is the administrator who made the change. The one field with no default: a change
	// nobody is recorded as having made is a change nobody can be asked about, and this is the
	// only moment the answer is known.
	ActorID string

	// ActorName and RoleName are the names those two had at the time, copied in rather than
	// joined. An audit trail is read months later, by which point the administrator may have been
	// deleted and the role renamed — and a trail showing a blank exactly where somebody is looking
	// is worse than no trail, because it reads as "nobody".
	ActorName string
	RoleName  string

	// Action is one of the Action* constants above.
	Action string

	// RoleID and PermissionKey are empty for the actions that do not concern one — a role
	// assignment names no permission.
	RoleID        string
	PermissionKey string

	// Detail carries what the other fields cannot: the scope a grant moved to, the name of a role
	// that no longer exists to be looked up.
	Detail string
}
