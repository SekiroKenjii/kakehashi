// Package telemetry wires OpenTelemetry traces and metrics to an OTLP collector.
//
// Everything except the service name is configured through the standard OTEL_* environment
// variables, which the exporters read themselves: endpoint, protocol, headers, timeouts, TLS.
// The standard names are kept because operators and collectors already know them.
package telemetry

import (
	"context"
	"errors"
	"fmt"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/exporters/otlp/otlpmetric/otlpmetricgrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracegrpc"
	"go.opentelemetry.io/otel/propagation"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/resource"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
)

// Options configures the export pipeline.
type Options struct {
	// ServiceName labels every span and metric this process emits.
	ServiceName string

	// Enabled reports whether an OTLP endpoint was configured. When false, Setup installs nothing
	// and returns a shutdown that does nothing, so the server runs identically with no collector
	// in front of it.
	Enabled bool
}

// Setup installs the global tracer and meter providers and returns their shutdown.
//
// The returned function must be called before the process exits, and given a context with a
// deadline. Spans are batched, so whatever is still in the buffer at exit is lost unless something
// flushes it, and the traces lost this way are exactly the ones from the requests that were in
// flight when things went wrong.
func Setup(ctx context.Context, opts Options) (func(context.Context) error, error) {
	noop := func(context.Context) error { return nil }
	if !opts.Enabled {
		return noop, nil
	}

	res, err := resource.Merge(
		resource.Default(),
		resource.NewSchemaless(attribute.String("service.name", opts.ServiceName)),
	)
	if err != nil {
		return noop, fmt.Errorf("build telemetry resource: %w", err)
	}

	traceExporter, err := otlptracegrpc.New(ctx)
	if err != nil {
		return noop, fmt.Errorf("create otlp trace exporter: %w", err)
	}
	tracerProvider := sdktrace.NewTracerProvider(
		sdktrace.WithBatcher(traceExporter),
		sdktrace.WithResource(res),
	)

	metricExporter, err := otlpmetricgrpc.New(ctx)
	if err != nil {
		// The tracer provider is already live at this point; shut it down rather than leaking its
		// batching goroutine for the lifetime of a process that is about to fail to start anyway.
		_ = tracerProvider.Shutdown(ctx)
		return noop, fmt.Errorf("create otlp metric exporter: %w", err)
	}
	meterProvider := sdkmetric.NewMeterProvider(
		sdkmetric.WithReader(sdkmetric.NewPeriodicReader(metricExporter)),
		sdkmetric.WithResource(res),
	)

	otel.SetTracerProvider(tracerProvider)
	otel.SetMeterProvider(meterProvider)

	// Without a propagator, incoming traceparent headers are ignored: the spans still arrive but
	// start new traces instead of joining the client's.
	otel.SetTextMapPropagator(propagation.NewCompositeTextMapPropagator(
		propagation.TraceContext{},
		propagation.Baggage{},
	))

	return func(ctx context.Context) error {
		return errors.Join(
			tracerProvider.Shutdown(ctx),
			meterProvider.Shutdown(ctx),
		)
	}, nil
}
