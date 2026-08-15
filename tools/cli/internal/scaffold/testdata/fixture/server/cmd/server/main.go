// Command server is the composition root: the only file that knows every module.
package main

import (
	"__GO_MODULE__/server/internal/app"
	// kakehashi:unit-example:begin
	"__GO_MODULE__/server/internal/modules/example"
	// kakehashi:unit-example:end
)

func main() {
	kernel := app.New("__APP_NAME_LOWER__")
	// kakehashi:unit-example:begin
	kernel.Mount(example.New())
	// kakehashi:unit-example:end
	kernel.Run()
}
