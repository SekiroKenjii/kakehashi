// Command kakehashi scaffolds a WinUI 3 client and a Go server from one template, and checks a
// machine for what building the result needs.
//
//	kakehashi new orderdesk --module github.com/me/orderdesk
//	kakehashi doctor
package main

import (
	"os"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/cli"
)

func main() {
	os.Exit(cli.Execute())
}
