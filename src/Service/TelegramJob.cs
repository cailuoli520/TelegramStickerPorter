[JobDetail("job_bot_monitor", Description = "机器人检测", GroupName = "default")]
[PeriodMinutes(5, TriggerId = "trigger_bot_monitor", Description = "每5分钟检测一次", RunOnStart = false)]
public class TelegramJob : IJob
{
    private readonly ILogger<TelegramJob> _logger;
    private readonly TelegramBotClientManager _telegramBotClientManager;

    public TelegramJob(
        ILogger<TelegramJob> logger,
        TelegramBotClientManager telegramBotClientManager)
    {
        _logger = logger;
        _telegramBotClientManager = telegramBotClientManager;
    }

    public async Task ExecuteAsync(JobExecutingContext context, CancellationToken stoppingToken)
    {
        _logger.LogInformation("检测机器人状态");
        var isAlive = await _telegramBotClientManager.CanPingTelegramAsync();

        if (!isAlive)
        {
            _logger.LogInformation("机器人不响应，重新创建中...");
            _telegramBotClientManager.CreatBot();
        }
        else
        {
            _logger.LogInformation("机器人正常运行中");
        }
    }
}