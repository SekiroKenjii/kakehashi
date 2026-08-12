package domain

import "time"

// Free strings rather than an enum, for the reason the account module's security-event kinds are:
// they cross the wire, an unrecognised one is printable and greppable, and every deployment that
// adds an action would otherwise have to widen a type.
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

// Not an aggregate root: written once, never modified, and nothing else has to change with it. The
// account module makes the same ruling about its SecurityEvent for the same reasons.
//
// It records the change rather than the resulting state. "Granted devops.sql at scope all" answers
// the question an access review actually asks — who decided this, and when — which a snapshot of
// the end state cannot.
type AuditEntry struct {
	ID         string
	OccurredAt time.Time

	// The one field with no default: a change nobody is recorded as having made is a change nobody
	// can be asked about, and this is the only moment the answer is known.
	ActorID string

	// The names those two had at the time, copied in rather than joined. An audit trail is read
	// months later, by which point the administrator may have been deleted and the role renamed —
	// and a trail showing a blank exactly where somebody is looking reads as "nobody".
	ActorName string
	RoleName  string

	Action string

	// Empty for the actions that do not concern one — a role assignment names no permission.
	RoleID        string
	PermissionKey string

	// What the other fields cannot carry: the scope a grant moved to, the name of a role that no
	// longer exists to be looked up.
	Detail string
}
