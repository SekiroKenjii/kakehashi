package domain

// LayoutPlan is a whole rearrangement, worked out and ready to write.
//
// It exists so that deciding and writing are two steps. The service compares what an administrator
// asked for against what is stored and reduces it to this; the store executes it in one transaction
// and decides nothing. Without the split, "which headings should be deleted" would be a question the
// store answered by reading — inside its own transaction, from data the service had already read.
//
// It lives in domain for the same reason Filter does: both the service that builds one and the store
// that executes one have to name it, and neither of those may depend on the other.
type LayoutPlan struct {
	// CreateGroups and UpdateGroups are separate because the outcome counts them separately, and
	// because a create that should have been an update is a bug worth being unable to write.
	CreateGroups []Group
	UpdateGroups []Group

	// DeleteGroups holds identifiers. The destinations under a deleted heading fall to ungrouped,
	// which the schema does with ON DELETE SET NULL rather than a statement here.
	DeleteGroups []string

	// Items are placements to write whole: heading, order, overrides and visibility together. Only
	// the ones that differ from what is stored are in here.
	Items []Placement
}

// IsEmpty reports whether this plan would change nothing.
//
// Worth asking before opening a transaction: a screen posts its whole arrangement, and most of the
// time every part of it is already true.
func (p LayoutPlan) IsEmpty() bool {
	return len(p.CreateGroups) == 0 &&
		len(p.UpdateGroups) == 0 &&
		len(p.DeleteGroups) == 0 &&
		len(p.Items) == 0
}
