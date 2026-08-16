// Package tui is what `kakehashi new` shows a person: the wizard it opens when it is given nothing
// to go on, and the progress it prints while the pipeline runs.
//
// Nothing here decides anything. The answers become a scaffold.Inputs and every rule about them
// still lives in scaffold, so a project made by answering questions and one made by passing flags
// are the same project.
package tui

import (
	"errors"
	"os"
)

// ErrNoTTY reports a terminal that cannot hold a conversation — a pipe, a CI runner, a process with
// its input redirected. The caller turns it into a usage error, which is what such a terminal needs:
// the flags to pass instead.
var ErrNoTTY = errors.New("this terminal cannot prompt")

// ErrCancelled reports that the wizard was closed rather than answered.
var ErrCancelled = errors.New("cancelled")

// interactive reports whether both ends of the conversation are a terminal. Output matters as much
// as input: a run whose output is piped into a file has nobody reading the questions.
func interactive() bool {
	return charDevice(os.Stdin) && charDevice(os.Stdout)
}

func charDevice(f *os.File) bool {
	info, err := f.Stat()
	return err == nil && info.Mode()&os.ModeCharDevice != 0
}
