namespace Gcg2OfflineServer.Services;

/// <summary>
/// 终端 + 文件双写日志。
/// 支持按大小轮转：单文件最大 20MB，保留最多 5 个历史文件。
/// </summary>
public class GameLogger
{
    private readonly string _logPath;
    private readonly object _lock = new();
    private readonly GameLogLevel _minLevel;
    private readonly long _maxFileSize;
    private readonly int _maxRetainedFiles;
    private long _currentSize;

    public GameLogger(string logPath, GameLogLevel minLevel = GameLogLevel.Info,
        long maxFileSize = 20 * 1024 * 1024, int maxRetainedFiles = 5)
    {
        _logPath = logPath;
        _minLevel = minLevel;
        _maxFileSize = maxFileSize;
        _maxRetainedFiles = maxRetainedFiles;

        var dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // 记录当前文件大小
        if (File.Exists(logPath))
            _currentSize = new FileInfo(logPath).Length;
    }

    public void Debug(string msg) => Log(GameLogLevel.Debug, msg);
    public void Info(string msg)  => Log(GameLogLevel.Info,  msg);
    public void Warn(string msg)  => Log(GameLogLevel.Warn,  msg);
    public void Error(string msg) => Log(GameLogLevel.Error, msg);

    private void Log(GameLogLevel level, string msg)
    {
        if (level < _minLevel) return;
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level,-5}] {msg}";
        var bytes = System.Text.Encoding.UTF8.GetByteCount(line + Environment.NewLine);

        lock (_lock)
        {
            Console.WriteLine(line);

            // 轮转检查：当前文件 + 新行超过上限则轮转
            if (_currentSize + bytes > _maxFileSize)
            {
                Rotate();
            }

            File.AppendAllText(_logPath, line + Environment.NewLine);
            _currentSize += bytes;
        }
    }

    /// <summary>
    /// 轮转日志：server.log -> server.log.1 -> server.log.2 -> ...
    /// 最旧的超过保留数量则删除。
    /// </summary>
    private void Rotate()
    {
        try
        {
            // 从最旧的开始删除/移动
            for (int i = _maxRetainedFiles; i >= 1; i--)
            {
                var src = i == 1 ? _logPath : $"{_logPath}.{i - 1}";
                var dst = $"{_logPath}.{i}";

                if (i == _maxRetainedFiles && File.Exists(dst))
                {
                    File.Delete(dst); // 删除最旧的
                }

                if (File.Exists(src))
                {
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(src, dst);
                }
            }

            // 创建新的空日志文件
            File.WriteAllText(_logPath, "");
            _currentSize = 0;
        }
        catch (Exception ex)
        {
            // 轮转失败不影响日志输出，降级为追加
            Console.WriteLine($"[LOGGER] Rotate failed: {ex.Message}");
        }
    }
}

public enum GameLogLevel { Debug, Info, Warn, Error }
