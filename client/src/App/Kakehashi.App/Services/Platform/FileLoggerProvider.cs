using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Kakehashi.App.Services.Platform;

/// <summary>
/// Writes the application log to a file under <c>%LOCALAPPDATA%\Kakehashi\logs</c>.
/// </summary>
/// <remarks>
/// Hand-rolled rather than a logging package: docs/adr/0008-hand-rolled-file-logger.md.
/// Invariants: a logging call never blocks the UI thread on disk, a failed write is swallowed
/// rather than becoming the crash, and this code never rotates or deletes log files.
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>(), 4096);
    private readonly string _path;
    private readonly Thread _writer;

    public FileLoggerProvider(LogLevel minimum)
    {
        Minimum = minimum;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kakehashi",
            "logs");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, $"app-{DateTime.Now:yyyy-MM-dd}.log");

        // IsBackground so it never keeps a closing app alive. Anything still queued at shutdown is
        // lost: hanging on exit to flush diagnostics is a worse bug than a missing last line.
        _writer = new Thread(Drain) { IsBackground = true, Name = "Kakehashi.FileLog" };
        _writer.Start();
    }

    /// <summary>The lowest level written. Everything below it is dropped before it is formatted.</summary>
    public LogLevel Minimum { get; }

    /// <summary>Where the log is being written, so the app can tell a user where to look.</summary>
    /// <remarks>Named LogPath rather than Path, which would shadow <see cref="System.IO.Path"/>.</remarks>
    public string LogPath => _path;

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this, categoryName);
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        // Bounded: a writer stuck on a locked file must not stop the process from exiting.
        _writer.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }

    internal void Enqueue(string line)
    {
        // TryAdd, not Add: when the queue is full the line is dropped rather than blocking whoever
        // logged it. A burst of logging must not become a UI freeze.
        _ = _queue.TryAdd(line);
    }

    private void Drain()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Locked or out of space. Nothing useful to do, and nowhere to report it to.
            }
            catch (UnauthorizedAccessException)
            {
                // Same.
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= _provider.Minimum && logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            ArgumentNullException.ThrowIfNull(formatter);

            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [")
                .Append(Level(logLevel))
                .Append("] ")
                .Append(_category)
                .Append(": ")
                .Append(formatter(state, exception))
                .AppendLine();

            // The whole exception, inner ones included. A log that recorded only the message would
            // answer "something failed" and not "where", which is the only part worth writing down.
            if (exception is not null)
            {
                line.AppendLine(exception.ToString());
            }

            _provider.Enqueue(line.ToString());
        }

        private static string Level(LogLevel level)
        {
            return level switch {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "???",
            };
        }
    }
}
