namespace TelegramStickerPorter;

public class TelegramBotClientManager
{
    private readonly ILogger<TelegramBotClientManager> _logger;
    private Bot _bot;
    private readonly object _lockObject = new();

    public TelegramBotClientManager(ILogger<TelegramBotClientManager> logger)
    {
        _logger = logger;
    }

    public Bot CreatBot()
    {
        lock (_lockObject)
        {
            try
            {
                StopBot();

                var data = App.GetConfig<TelegramOptions>("Telegram");
                var basePath = AppContext.BaseDirectory;
                var dbPath = Path.Combine(basePath, "TelegramBot.sqlite");
                var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");

                _bot = new Bot(
                    data.BotToken,
                    data.ApiId,
                    data.ApiHash,
                    connection,
                    SqlCommands.Sqlite);

                _logger.LogInformation("创建新机器人实例成功");
                return _bot;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建机器人实例失败");
                throw Oops.Oh(ex, "启动机器人时发生错误");
            }
        }
    }

    public Bot GetBot()
    {
        return _bot ?? throw new InvalidOperationException("机器人实例未初始化");
    }

    public void StopBot()
    {
        if (_bot == null) return;

        try
        {
            _bot.Dispose();
            _logger.LogInformation("机器人实例已释放");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放机器人实例时出错");
        }
        finally
        {
            _bot = null;
        }
    }

    public async Task<bool> CanPingTelegramAsync()
    {
        if (_bot == null)
        {
            _logger.LogWarning("机器人实例不存在，无法检测连接");
            return false;
        }

        try
        {
            var cmds = await _bot.GetMyCommands();
            return cmds != null;
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("机器人实例已被释放，无法检测连接");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检测机器人连接失败");
            return false;
        }
    }
}