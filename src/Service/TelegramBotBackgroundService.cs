namespace TelegramStickerPorter;

public class TelegramBotBackgroundService : BackgroundService
{
    private readonly TelegramBotClientManager _telegramBotClientManager;
    private readonly ILogger<TelegramBotBackgroundService> _logger;
    private readonly StickerService _stickerService;
    private readonly SemaphoreSlim _restartLock = new(1, 1);

    public TelegramBotBackgroundService(
        ILogger<TelegramBotBackgroundService> logger,
        TelegramBotClientManager telegramBotClientManager,
        StickerService stickerService)
    {
        _logger = logger;
        _telegramBotClientManager = telegramBotClientManager;
        _stickerService = stickerService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EnsureBotInitializedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("机器人后台服务已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "机器人启动失败");
        }
    }

    public Task EnsureBotInitializedAsync(CancellationToken cancellationToken = default)
        => InitializeInternalAsync(forceRestart: false, cancellationToken);

    public Task RestartBotAsync(CancellationToken cancellationToken = default)
        => InitializeInternalAsync(forceRestart: true, cancellationToken);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在停止机器人后台服务");
        _telegramBotClientManager.StopBot();
        await base.StopAsync(cancellationToken);
    }

    private async Task InitializeInternalAsync(bool forceRestart, CancellationToken cancellationToken)
    {
        await _restartLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRestart && _telegramBotClientManager.HasActiveBot)
            {
                _logger.LogDebug("检测到机器人已在线，跳过初始化");
                return;
            }

            _logger.LogInformation(forceRestart ? "正在重新初始化机器人..." : "正在初始化机器人...");
            var bot = _telegramBotClientManager.CreateBot();
            var me = await bot.GetMe();
            _logger.LogInformation($"机器人启动: @{me.Username}");

            await ConfigureBotAsync(bot);
        }
        finally
        {
            _restartLock.Release();
        }
    }

    private async Task ConfigureBotAsync(Bot bot)
    {
        var commands = new[]
        {
            new Telegram.Bot.Types.BotCommand { Command = "start", Description = "启动机器人" },
            new Telegram.Bot.Types.BotCommand { Command = "info", Description = "关于" },
            new Telegram.Bot.Types.BotCommand { Command = "download", Description = "下载贴纸包" },
            new Telegram.Bot.Types.BotCommand { Command = "help_download", Description = "下载帮助" },
        };

        foreach (var cmd in commands)
        {
            _logger.LogInformation($"命令：{cmd.Command} 描述：{cmd.Description}");
        }

        await bot.SetMyCommands(commands, new BotCommandScopeAllPrivateChats());
        await bot.DropPendingUpdates();
        _logger.LogInformation("机器人丢弃未处理的更新");

        ConfigureErrorHandling(bot);
        ConfigureMessageHandling(bot);
    }

    private void ConfigureErrorHandling(Bot bot)
    {
        bot.WantUnknownTLUpdates = true;
        bot.OnError += (e, s) =>
        {
            _logger.LogError($"机器人错误: {e}");
            return Task.CompletedTask;
        };
    }

    private void ConfigureMessageHandling(Bot bot)
    {
        bot.OnMessage += async (msg, type) => await OnMessageAsync(bot, msg, type);
        bot.OnUpdate += update =>
        {
            _logger.LogInformation("机器人处理更新");
            ProcessUpdate(bot, update);
            return Task.CompletedTask;
        };

        _logger.LogInformation("机器人监听中...");
    }

    private async Task OnMessageAsync(Bot bot, WTelegram.Types.Message msg, UpdateType type)
    {
        if (msg.Chat.Type != ChatType.Group && msg.Chat.Type != ChatType.Supergroup)
        {
            await HandlePrivateAsync(bot, msg);
        }
    }

    private void ProcessUpdate(Bot bot, WTelegram.Types.Update update)
    {
        if (update.Type != UpdateType.Unknown) return;

        if (update.TLUpdate is TL.UpdateDeleteChannelMessages udcm)
            _logger.LogInformation($"{udcm.messages.Length} 条消息被删除，来源：{bot.Chat(udcm.channel_id)?.Title}");
        else if (update.TLUpdate is TL.UpdateDeleteMessages udm)
            _logger.LogInformation($"{udm.messages.Length} 条消息被删除，来源：用户或小型私聊群组");
        else if (update.TLUpdate is TL.UpdateReadChannelOutbox urco)
            _logger.LogInformation($"某人阅读了 {bot.Chat(urco.channel_id)?.Title} 的消息，直到消息 ID: {urco.max_id}");
    }

    private async Task HandlePrivateAsync(Bot bot, WTelegram.Types.Message msg)
    {
        if (msg.Text == null) return;

        var text = msg.Text.ToLower();

        if (text.StartsWith("/start") || text == "/clonepack" || text == "clonepack" || text == "克隆" || text == "贴纸" || text == "tiezhi" || text == "表情" || text == "biaoqing" || text == "emoji" || text == "stickers")
        {
            await _stickerService.SendStickerInstructionsAsync(bot, msg);
        }
        else if (text.StartsWith("克隆#"))
        {
            await _stickerService.HandleCloneCommandAsync(bot, msg);
        }
        else if (text.StartsWith("/info"))
        {
            await _stickerService.SendStickerInfoAsync(bot, msg);
        }
        else if (text.StartsWith("下载#") || text.StartsWith("/download") || text.Contains("download"))
        {
            await _stickerService.HandleDownloadCommandAsync(bot, msg);
        }
        else if (text.StartsWith("/help_download") || text.Contains("帮助下载"))
        {
            await _stickerService.SendDownloadInstructionsAsync(bot, msg);
        }
    }
}