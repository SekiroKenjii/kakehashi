// Package tui holds the wizard `kakehashi new` opens when it is given nothing to go on. Phase 4
// builds it; the package exists now so that the command wiring around it does not move later.
package tui

import (
	"errors"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/scaffold"
)

// ErrNoWizard reports that this build cannot ask. Callers turn it into a usage error, which is
// what a terminal that cannot prompt needs anyway.
var ErrNoWizard = errors.New("the wizard is not built yet")

// Wizard collects the inputs interactively.
func Wizard() (scaffold.Inputs, error) {
	return scaffold.Inputs{}, ErrNoWizard
}
