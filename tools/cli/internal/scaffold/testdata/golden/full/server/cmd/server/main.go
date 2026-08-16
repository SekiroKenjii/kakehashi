// Command server is the composition root: the only file that knows every module.
package main

import (
	"example.com/smokeapp/server/internal/app"
	// kakehashi:unit-example:begin
	"example.com/smokeapp/server/internal/modules/example"
	// kakehashi:unit-example:end
)

func main() {
	kernel := app.New("smokeapp")
	// kakehashi:unit-example:begin
	kernel.Mount(example.New())
	// kakehashi:unit-example:end
	kernel.Run()
}
