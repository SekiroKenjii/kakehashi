// Package telemetry wires OpenTelemetry traces and metrics to an OTLP collector.
//
// Everything except the service name is configured through the standard OTEL_* environment
// variables, which the exporters read themselves: endpoint, protocol, headers, timeouts, TLS. Those
// variables are a specification several tools already implement, and an operator who knows
// OTEL_EXPORTER_OTLP_HEADERS should not have to discover that this server invented its own name.
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

type Options struct {
	ServiceName string

	// When false, Setup installs nothing and returns a shutdown that does nothing, so the server
	// runs identically with no collector in front of it.
	Enabled bool
}

// The returned shutdown must be called before the process exits, and given a context with a
// deadline. Spans are batched, so whatever is still in the buffer at exit is lost unless something
// flushes it — exactly the traces from the requests in flight when things went wrong.
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
		// The tracer provider is already live; shut it down rather than leaking its batching
		// goroutine.
		_ = tracerProvider.Shutdown(ctx)
		return noop, fmt.Errorf("create otlp metric exporter: %w", err)
	}
	meterProvider := sdkmetric.NewMeterProvider(
		sdkmetric.WithReader(sdkmetric.NewPeriodicReader(metricExporter)),
		sdkmetric.WithResource(res),
	)

	otel.SetTracerProvider(tracerProvider)
	otel.SetMeterProvider(meterProvider)

	// Without a propagator, incoming traceparent headers are ignored and every request starts a
	// new trace: the spans still arrive, they just do not join up with the client's.
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
