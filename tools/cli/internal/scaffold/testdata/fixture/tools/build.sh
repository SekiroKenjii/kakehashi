#!/usr/bin/env bash
# Builds __APP_NAME__.
set -euo pipefail
go build -o "__APP_NAME_LOWER__" ./cmd/server
