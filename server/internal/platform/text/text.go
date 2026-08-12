// Package text holds the string measurements the domains share.
//
// Here rather than in one module because the rule it encodes is the database's: three domains cap
// fields against nvarchar columns, and archlint rightly stops notes/domain from importing
// account/domain to reach a helper.
package text

import "unicode/utf16"

// UTF16Len is how many units a string takes in an nvarchar column.
//
// Not the rune count, which is what these checks used. nvarchar(n) counts UTF-16 code units, and a
// character outside the Basic Multilingual Plane — an emoji, an old CJK ideograph — takes two of
// them. Counting runes let a value pass the domain and fail the INSERT, turning a message somebody
// could act on into an opaque 500 from the driver.
func UTF16Len(s string) int {
	return len(utf16.Encode([]rune(s)))
}
