# Build the server as a static binary and ship it on a base with nothing else on it.
#
# The build context is ./server, not the repository root: the generated protobuf code is committed
# under internal/gen, so nothing outside this directory is needed to compile. proto/ is a build-time
# input to `buf generate`, not to `go build`.

FROM golang:1.26-alpine AS build

WORKDIR /src

# Dependencies first, in their own layer. They change far less often than the code, so an edit to a
# handler does not re-download the module cache.
COPY go.mod go.sum ./
RUN go mod download

COPY . .

# Stamped rather than read at runtime, so a binary someone finds on a server can say where it came
# from. Defaults keep a plain `docker build` working; compose and CI pass the real values.
ARG VERSION=0.1.0-dev
ARG COMMIT=unknown
ARG DATE=unknown

# CGO off is what makes the result static, and static is what makes the scratch-like base below
# possible. There is no cgo dependency to lose: the SQL Server and Mongo drivers are pure Go.
# -trimpath keeps build-machine paths out of the binary, which makes the build reproducible.
RUN CGO_ENABLED=0 GOOS=linux go build \
      -trimpath \
      -ldflags "-s -w \
        -X __GO_MODULE__/server/internal/app.Version=${VERSION} \
        -X __GO_MODULE__/server/internal/app.Commit=${COMMIT} \
        -X __GO_MODULE__/server/internal/app.Date=${DATE}" \
      -o /out/__APP_NAME_LOWER__ ./cmd/server

# distroless static: libc-free, shell-free, package-manager-free. Nothing to exploit and nothing to
# patch, at the cost of not being able to `docker exec` into it and poke around. That trade is
# right for a server and wrong for a debugging session; use the :debug tag when you need the latter.
FROM gcr.io/distroless/static-debian12:nonroot

COPY --from=build /out/__APP_NAME_LOWER__ /__APP_NAME_LOWER__

# Non-root by default. The port is above 1024 precisely so that it can be.
USER nonroot:nonroot
EXPOSE 8080

ENTRYPOINT ["/__APP_NAME_LOWER__"]
