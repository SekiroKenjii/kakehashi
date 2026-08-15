package checks

import (
	"context"
	"net/http"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/template"
)

// gitHubReachable is advisory because a cached template scaffolds without it, which is what
// --offline is for.
func gitHubReachable() Check {
	return Check{
		Name:  "GitHub reachable",
		Level: Advisory,
		probe: func(ctx context.Context) (Status, string, string) {
			const url = "https://api.github.com"
			request, err := http.NewRequestWithContext(ctx, http.MethodHead, url, nil)
			if err != nil {
				return Warn, err.Error(), ""
			}

			response, err := http.DefaultClient.Do(request)
			if err != nil {
				return Warn, "unreachable", "fetching a template needs it; --offline uses the cache in " +
					template.DefaultCacheDir()
			}
			defer response.Body.Close()

			if response.StatusCode >= http.StatusInternalServerError {
				return Warn, response.Status, ""
			}
			return Pass, url, ""
		},
	}
}
