# 0008. The file logger is hand-rolled rather than a logging package

Date: 2026-08-12
Status: accepted

## Context

The host registered only `AddDebug()`, which writes through `OutputDebugString` — visible to an
attached debugger and to nobody else. A packaged build handed to a tester therefore produced no
log at all, and the first question after any crash report ("what does the log say?") had no
answer. The job to fill that gap was a timestamp, a line of text, and a file handle, which did
not justify a logging-package dependency.

## Decision

`FileLoggerProvider` (client/src/App/Kakehashi.App/Services/Platform/FileLoggerProvider.cs) is a
hand-rolled `ILoggerProvider`. It appends to one file per day under
`%LOCALAPPDATA%\Kakehashi\logs` and never deletes old files. Lines enter a bounded queue (4096)
drained by a single background thread; `TryAdd` drops lines when the queue is full, and write
failures (`IOException`, `UnauthorizedAccessException`) are swallowed. Exceptions are logged in
full, inner exceptions included, not message-only.

## Consequences

Every machine that runs the app carries a readable log, and the provider exposes `LogPath` so the
app can tell a user where to look. Invariants a future change must respect: a logging call never
blocks the UI thread on disk, a broken log never becomes the crash, and this code never rotates
or deletes log files — a log that rotates itself away has deleted the evidence by the time
somebody asks for it. Costs accepted: files accumulate until removed by hand; lines still queued
at shutdown are lost (the writer thread is `IsBackground` and `Dispose` joins for at most two
seconds); bursts beyond the queue bound are silently dropped; `BeginScope` is a no-op.
