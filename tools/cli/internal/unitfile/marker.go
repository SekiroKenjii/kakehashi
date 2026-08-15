package unitfile

import "regexp"

// idPattern is what a unit id may be. It ends up inside a marker comment and inside a regular
// expression, so it is restricted to what is safe in both.
var idPattern = regexp.MustCompile(`^[a-z][a-z0-9-]*$`)

// Region returns the markers that fence the unit's wiring: `kakehashi:unit-<id>:begin` and
// `kakehashi:unit-<id>:end`, in whatever comment syntax the file around them uses. These strings
// survive into a scaffolded project on purpose — they are the generator's namespace, not the
// application's, and the CLI reads them there to add and remove modules.
func (u *Unit) Region() (begin, end *regexp.Regexp) {
	return regexp.MustCompile(`kakehashi:unit-` + regexp.QuoteMeta(u.ID) + `:begin`),
		regexp.MustCompile(`kakehashi:unit-` + regexp.QuoteMeta(u.ID) + `:end`)
}
