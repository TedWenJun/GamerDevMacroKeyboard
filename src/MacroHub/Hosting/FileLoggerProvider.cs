using System.Collections.Concurrent;
using System.Text;

namespace MacroHub.Hosting;

/// <summary>
/// Minimal rolling file logger: one line per entry, rotated at <see cref="MaxBytes"/> keeping <see cref="Keep"/> old files.
/// MacroHub normally runs without a console (installed / autostart), so this is where diagnostics end up.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, Logger> _loggers = new();
    private StreamWriter? _writer;

    public long MaxBytes { get; init; } = 5 * 1024 * 1024;
    public int Keep { get; init; } = 3;
    public string FilePath => _path;

    public FileLoggerProvider(string directory, string fileName = "macrohub.log")
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, fileName);
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new Logger(this, name));

    private void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                _writer ??= Open();
                _writer.WriteLine(line);
                if (_writer.BaseStream.Length > MaxBytes) Rotate();
            }
            catch (IOException) { _writer?.Dispose(); _writer = null; }
        }
    }

    private StreamWriter Open() =>
        new(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };

    private void Rotate()
    {
        _writer?.Dispose();
        _writer = null;
        for (int i = Keep - 1; i >= 1; i--)
        {
            var from = $"{_path}.{i}";
            if (File.Exists(from)) File.Move(from, $"{_path}.{i + 1}", overwrite: true);
        }
        File.Move(_path, $"{_path}.1", overwrite: true);
    }

    public void Dispose()
    {
        lock (_gate) { _writer?.Dispose(); _writer = null; }
    }

    private sealed class Logger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var level = logLevel switch
            {
                LogLevel.Trace => "trce", LogLevel.Debug => "dbug", LogLevel.Information => "info",
                LogLevel.Warning => "warn", LogLevel.Error => "fail", _ => "crit",
            };
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {category}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            owner.Write(line);
        }
    }
}
