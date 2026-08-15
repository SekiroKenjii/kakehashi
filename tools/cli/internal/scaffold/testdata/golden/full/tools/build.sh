#!/usr/bin/env bash
# Builds SmokeApp.
set -euo pipefail
go build -o "smokeapp" ./cmd/server
