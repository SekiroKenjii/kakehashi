package checks

import "context"

// dockerDaemon asks the daemon rather than the client: `docker --version` answers on a machine
// where nothing can actually start, and starting the compose stack is the only reason this is here.
func dockerDaemon() Check {
	return Check{
		Name:  "Docker daemon",
		Level: Advisory,
		probe: func(ctx context.Context) (Status, string, string) {
			version, err := output(ctx, "docker", "info", "--format", "{{.ServerVersion}}")
			if err != nil {
				return Warn, "not running", "needed only for 'docker compose up': https://docs.docker.com/get-docker/"
			}
			return Pass, version, ""
		},
	}
}
