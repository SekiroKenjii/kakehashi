// Command server is the composition root: the only file that knows every module.
package main

import (
	"example.com/smokeapp/server/internal/app"
)

func main() {
	kernel := app.New("smokeapp")
	kernel.Run()
}
