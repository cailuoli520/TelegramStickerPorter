[JobDetail("job_bot_monitor", Description = "机器人检测", GroupName = "default")]
[PeriodMinutes(5, TriggerId = "trigger_bot_monitor", Description = "每5分钟检测一次", RunOnStart = false)]
public class TelegramJob : IJob
{
    private readonly ILogger<TelegramJob> _logger;
    private readonly TelegramBotClientManager _telegramBotClientManager;
    private readonly TelegramBotBackgroundService _telegramBotBackgroundService;

    public TelegramJob(
        ILogger<TelegramJob> logger,
        TelegramBotClientManager telegramBotClientManager,
        TelegramBotBackgroundService telegramBotBackgroundService)
    {
        _logger = logger;
        _telegramBotClientManager = telegramBotClientManager;
        _telegramBotBackgroundService = telegramBotBackgroundService;
    }

    public async Task ExecuteAsync(JobExecutingContext context, CancellationToken stoppingToken)
    {
        _logger.LogInformation("检测机器人状态");
        var isAlive = await _telegramBotClientManager.CanPingTelegramAsync();

        if (!isAlive)
        {
            _logger.LogInformation("机器人不响应，尝试重新初始化...");
            try
            {
                await _telegramBotBackgroundService.RestartBotAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重新初始化机器人实例失败");
            }
        }
        else
        {
            _logger.LogInformation("机器人正常运行中");
        }
    }
}