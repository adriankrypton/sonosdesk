using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SonosDesk.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const int RetainedFiles = 7;

    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Task _writer;

    public FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, ShortCategory(categoryName));

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        try
        {
            _writer.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Logging must never block or crash shutdown.
        }
    }

    private void Enqueue(string line) => _queue.Writer.TryWrite(line);

    private async Task WriteLoopAsync()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            CleanupOldFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        await foreach (var line in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                var path = Path.Combine(_directory, $"sonosdesk-{DateTime.Now:yyyyMMdd}.log");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxFileBytes)
                {
                    continue;
                }

                await File.AppendAllTextAsync(path, line, Encoding.UTF8).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Disk full or file locked: drop the line.
            }
        }
    }

    private void CleanupOldFiles()
    {
        foreach (var file in new DirectoryInfo(_directory).GetFiles("sonosdesk-*.log")
                     .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                     .Skip(RetainedFiles))
        {
            file.Delete();
        }
    }

    private static string ShortCategory(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot >= 0 ? category[(dot + 1)..] : category;
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var builder = new StringBuilder(256)
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                .Append(" [").Append(Level(logLevel)).Append("] ")
                .Append(category).Append(": ")
                .Append(formatter(state, exception));
            if (exception is not null)
            {
                builder.AppendLine().Append(exception.GetType().FullName).Append(": ").Append(exception.Message)
                    .AppendLine().Append(exception.StackTrace);
            }

            provider.Enqueue(builder.AppendLine().ToString());
        }

        private static string Level(LogLevel level) => level switch
        {
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
