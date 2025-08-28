namespace TelegramStickerPorter;

public class TelegramBotBackgroundService : BackgroundService
{
    private readonly TelegramBotClientManager _telegramBotClientManager;
    private readonly ILogger<TelegramBotBackgroundService> _logger;
    private readonly StickerService _stickerService;

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
            _logger.LogInformation("正在启动机器人...");
            var bot = _telegramBotClientManager.CreatBot();
            var me = await bot.GetMe();
            _logger.LogInformation($"机器人启动: @{me.Username}");

            await InitializeBotAsync(bot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "机器人启动失败");
        }
    }

    private async Task InitializeBotAsync(Bot bot)
    {
        var commands = new[]
        {
            new Telegram.Bot.Types.BotCommand { Command = "start", Description = "启动机器人" },
            new Telegram.Bot.Types.BotCommand { Command = "info", Description = "关于" },
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
    }
}