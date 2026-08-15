# __APP_NAME__ server — build, test, lint.
#
# Run `make` on its own to see what is available. Everything here is also a plain go command; the
# targets exist so that CI and a developer run the identical thing, which is the only way the
# "works on my machine" gap stays closed.

BINARY  := __APP_NAME_LOWER__
MODULE  := __GO_MODULE__/server

VERSION ?= $(shell git describe --tags --always --dirty 2>/dev/null || echo 0.1.0-dev)
COMMIT  ?= $(shell git rev-parse --short HEAD 2>/dev/null || echo unknown)
DATE    ?= $(shell date -u +%Y-%m-%dT%H:%M:%SZ)

BIN_DIR := bin

LDFLAGS := -X '$(MODULE)/internal/app.Version=$(VERSION)' \
           -X '$(MODULE)/internal/app.Commit=$(COMMIT)' \
           -X '$(MODULE)/internal/app.Date=$(DATE)'

.DEFAULT_GOAL := help

## help: list the targets
help:
	@echo "$(BINARY) — $(VERSION)"
	@echo
	@sed -n 's/^## //p' $(MAKEFILE_LIST) | awk -F': ' '{printf "  \033[36m%-14s\033[0m %s\n", $$1, $$2}'

## build: compile the server into bin/
build:
	@mkdir -p $(BIN_DIR)
	CGO_ENABLED=0 go build -trimpath -ldflags "$(LDFLAGS)" -o $(BIN_DIR)/$(BINARY) ./cmd/server
	@echo "built $(BIN_DIR)/$(BINARY) ($(VERSION))"

## test: run the unit tests
#
# None of them need a database: the domain, service and platform layers are free of I/O on purpose.
# Anything that does need one is an integration test, tagged and run separately.
test:
	go test ./...

## cover: run the tests and open a coverage report
cover:
	go test -coverprofile=coverage.out ./...
	go tool cover -html=coverage.out

## lint: vet, formatting check, and the architecture boundaries
lint: vet fmt-check arch

vet:
	go vet ./...

fmt-check:
	@unformatted=$$(gofmt -l cmd internal tools); \
	if [ -n "$$unformatted" ]; then \
		echo "these files are not gofmt'd:"; echo "$$unformatted"; exit 1; \
	fi

## arch: check the module boundaries hold
#
# This is the one that keeps the monolith modular. See tools/archlint.
arch:
	@go run ./tools/archlint

## fmt: format the code
fmt:
	gofmt -w cmd internal tools

## tidy: sync go.mod with the imports
tidy:
	go mod tidy

## clean: remove build artefacts
clean:
	rm -rf $(BIN_DIR) coverage.out

.PHONY: help build test cover lint vet fmt-check arch fmt tidy clean
