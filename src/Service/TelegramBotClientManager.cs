namespace TelegramStickerPorter;

public class TelegramBotClientManager
{
    private readonly ILogger<TelegramBotClientManager> _logger;
    private readonly TelegramOptions _options;
    private Bot _bot;
    private readonly object _lockObject = new();

    public TelegramBotClientManager(ILogger<TelegramBotClientManager> logger)
    {
        _logger = logger;
        _options = App.GetConfig<TelegramOptions>("Telegram") ?? throw Oops.Oh("未在配置中找到 Telegram 节点");
        ValidateOptions(_options);
    }

    public bool HasActiveBot
    {
        get
        {
            lock (_lockObject)
            {
                return _bot != null;
            }
        }
    }

    public Bot CreateBot()
    {
        lock (_lockObject)
        {
            try
            {
                StopBotInternal();

                var basePath = AppContext.BaseDirectory;
                var dbPath = Path.Combine(basePath, "TelegramBot.sqlite");
                var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");

                _bot = new Bot(
                    _options.BotToken,
                    _options.ApiId,
                    _options.ApiHash,
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
        lock (_lockObject)
        {
            StopBotInternal();
        }
    }

    public async Task<bool> CanPingTelegramAsync()
    {
        Bot bot;
        lock (_lockObject)
        {
            bot = _bot;
        }

        if (bot == null)
        {
            _logger.LogWarning("机器人实例不存在，无法检测连接");
            return false;
        }

        try
        {
            var cmds = await bot.GetMyCommands();
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

    private void StopBotInternal()
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

    private static void ValidateOptions(TelegramOptions options)
    {
        if (options.ApiId <= 0)
            throw Oops.Oh("Telegram:ApiId 配置必须为正整数");

        if (string.IsNullOrWhiteSpace(options.ApiHash))
            throw Oops.Oh("Telegram:ApiHash 配置不能为空");

        if (string.IsNullOrWhiteSpace(options.BotToken))
            throw Oops.Oh("Telegram:BotToken 配置不能为空");
    }
}