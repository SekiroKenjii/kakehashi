using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Kakehashi.App.Services.Platform {
  // Writes the application log to a file under %LOCALAPPDATA%\Kakehashi\logs.
  //
  // This exists because the alternative was nothing. The host registered only
  // AddDebug(), which writes through OutputDebugString — visible to a debugger and
  // to nobody else. A packaged build handed to a tester therefore produced no record at all, and
  // the first question after any crash report ("what does the log say?") had no answer.
  //
  // Hand-rolled rather than a logging package, because the whole job is a line of text and a file
  // handle. One file per day, appended, and never deleted by this code: a log that rotates itself
  // away is a log that has deleted the evidence by the time somebody asks for it.
  //
  // Writes are queued and drained on one background thread, so a logging call never blocks the UI
  // thread on disk. Failing to write is swallowed — a broken log must not become the crash.
  public sealed class FileLoggerProvider : ILoggerProvider {
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>(), 4096);
    private readonly string _path;
    private readonly Thread _writer;

    public FileLoggerProvider(LogLevel minimum) {
      Minimum = minimum;

      var directory = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "Kakehashi",
          "logs");
      Directory.CreateDirectory(directory);
      _path = Path.Combine(directory, $"app-{DateTime.Now:yyyy-MM-dd}.log");

      // Background, and IsBackground so it never keeps a closing app alive. Anything still queued
      // at shutdown is lost, which is the right trade for a log: an app that hangs on exit to
      // flush its diagnostics is a worse bug than a missing last line.
      _writer = new Thread(Drain) { IsBackground = true, Name = "Kakehashi.FileLog" };
      _writer.Start();
    }

    // The lowest level written. Everything below it is dropped before it is formatted.
    public LogLevel Minimum { get; }

    // Where the log is being written, so the app can tell a user where to look.
    // Named LogPath rather than Path, which would shadow System.IO.Path.
    public string LogPath => _path;

    public ILogger CreateLogger(string categoryName) {
      return new FileLogger(this, categoryName);
    }

    public void Dispose() {
      _queue.CompleteAdding();
      // Bounded: a writer stuck on a locked file must not stop the process from exiting.
      _writer.Join(TimeSpan.FromSeconds(2));
      _queue.Dispose();
    }

    internal void Enqueue(string line) {
      // TryAdd, not Add: when the queue is full the line is dropped rather than blocking whoever
      // logged it. A burst of logging must not become a UI freeze.
      _ = _queue.TryAdd(line);
    }

    private void Drain() {
      foreach (var line in _queue.GetConsumingEnumerable()) {
        try {
          File.AppendAllText(_path, line, Encoding.UTF8);
        } catch (IOException) {
          // Locked or out of space. Nothing useful to do, and nowhere to report it to.
        } catch (UnauthorizedAccessException) {
          // Same.
        }
      }
    }

    private sealed class FileLogger : ILogger {
      private readonly FileLoggerProvider _provider;
      private readonly string _category;

      public FileLogger(FileLoggerProvider provider, string category) {
        _provider = provider;
        _category = category;
      }

      public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
        return null;
      }

      public bool IsEnabled(LogLevel logLevel) {
        return logLevel >= _provider.Minimum && logLevel != LogLevel.None;
      }

      public void Log<TState>(
          LogLevel logLevel,
          EventId eventId,
          TState state,
          Exception? exception,
          Func<TState, Exception?, string> formatter) {
        if (!IsEnabled(logLevel)) {
          return;
        }
        ArgumentNullException.ThrowIfNull(formatter);

        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(Level(logLevel)).Append("] ")
            .Append(_category)
            .Append(": ")
            .Append(formatter(state, exception))
            .AppendLine();

        // The whole exception, inner ones included. A log that recorded only the message would
        // answer "something failed" and not "where", which is the only part worth writing down.
        if (exception is not null) {
          line.AppendLine(exception.ToString());
        }

        _provider.Enqueue(line.ToString());
      }

      private static string Level(LogLevel level) {
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
}
