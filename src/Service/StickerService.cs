namespace TelegramStickerPorter;

public class StickerService
{
    private readonly ILogger<StickerService> _logger;
    private readonly MessageService _messageService;

    public StickerService(ILogger<StickerService> logger, MessageService messageService)
    {
        _logger = logger;
        _messageService = messageService;
    }

    public async Task SendStickerInstructionsAsync(Bot bot, Telegram.Bot.Types.Message msg)
    {
        var messageText = new StringBuilder()
            .AppendLine("💎 <b>贴纸/表情克隆使用说明</b> 💎")
            .AppendLine()
            .AppendLine("请输入您想要的目标贴纸包（或表情包）的名称，以及需要克隆的原始贴纸包（或表情包）链接，格式如下：")
            .AppendLine()
            .AppendLine("<code>克隆#您的贴纸包（或表情包）名称#需要克隆的贴纸包（或表情包）链接</code>")
            .AppendLine()
            .AppendLine("例如：")
            .AppendLine("<code>克隆#我的可爱表情包#https://t.me/addstickers/pack_bafb8ef1_by_stickerporter_bot</code>")
            .AppendLine()
            .AppendLine("<code>克隆#我的酷酷的贴纸包#https://t.me/addemoji/pack_7f810f59_by_stickerporter_bot</code>")
            .AppendLine()
            .AppendLine("🔹 <b>克隆</b>：命令前缀，触发克隆操作。")
            .AppendLine("🔹 <b>您的贴纸包（或表情包）名称</b>：您希望克隆后新贴纸包（或表情包）的名称。")
            .AppendLine("🔹 <b>需要克隆的贴纸包（或表情包）链接</b>：原始贴纸（或表情包）的链接。")
            .AppendLine()
            .AppendLine("请确保信息填写正确，以便程序顺利克隆哦～ 🚀")
            .ToString();

        await _messageService.SendMessageAsync(bot, msg.Chat.Id, messageText, replyParameters: msg);
    }

    public async Task SendStickerInfoAsync(Bot bot, Telegram.Bot.Types.Message msg)
    {
        var messageText = new StringBuilder()
            .AppendLine("💎 <b>开源地址:</b> 💎")
            .AppendLine("https://github.com/Riniba/TelegramStickerPorter")
            .AppendLine()
            .ToString();

        await _messageService.SendMessageAsync(bot, msg.Chat.Id, messageText, replyParameters: msg);
    }

    public async Task HandleCloneCommandAsync(Bot bot, Telegram.Bot.Types.Message msg)
    {
        try
        {
            string[] parts = msg.Text.Split('#');

            if (parts.Length != 3)
            {
                var errorMsg = new StringBuilder()
                    .AppendLine("格式错误！请使用正确的格式：")
                    .Append("克隆#您的贴纸包（或表情包）名称#需要克隆的贴纸包（或表情包）链接")
                    .ToString();

                await _messageService.SendMessageAsync(bot, msg.Chat.Id, errorMsg, replyParameters: msg);
                return;
            }

            string newStickerSetTitle = parts[1];
            string stickerUrl = parts[2];

            if (!stickerUrl.StartsWith("https://t.me/add"))
            {
                await _messageService.SendMessageAsync(bot, msg.Chat.Id,
                    "贴纸链接格式错误！链接应该以 https://t.me/add 开头",
                    replyParameters: msg);
                return;
            }

            string sourceStickerSetName = stickerUrl
                .Replace("https://t.me/addstickers/", "")
                .Replace("https://t.me/addemoji/", "");

            var statusMessage = $"✨ 正在开始克隆贴纸包，请稍候...\n此过程可能需要几分钟。";
            var statusMessageId = await _messageService.SendMessageAsync(bot, msg.Chat.Id, statusMessage);

            _ = Task.Run(async () => await ProcessCloneStickerTaskAsync(
                bot, msg, statusMessageId, sourceStickerSetName, newStickerSetTitle));
        }
        catch (Exception ex)
        {
            var errorBuilder = $"[错误] 发生异常: {ex.Message}";
            _logger.LogError(ex, "处理克隆命令时发生异常");
            await _messageService.SendMessageAsync(bot, msg.Chat.Id, errorBuilder);
        }
    }

    private async Task ProcessCloneStickerTaskAsync(
        Bot bot,
        Telegram.Bot.Types.Message msg,
        int statusMessageId,
        string sourceStickerSetName,
        string newStickerSetTitle)
    {
        List<string> stickerErrors = new List<string>();

        try
        {
            var me = await bot.GetMe();
            string botUsername = me.Username?.ToLower();
            string newPackName = GeneratePackName(botUsername);

            var sourceSet = await bot.GetStickerSet(sourceStickerSetName);

            var statusBuilder = new StringBuilder()
                .AppendLine("📦 源贴纸包信息:")
                .AppendLine($"标题: {sourceSet.Title}")
                .AppendLine($"贴纸数量: {sourceSet.Stickers.Length}")
                .AppendLine($"类型: {sourceSet.StickerType}")
                .AppendLine()
                .Append("🔄 正在准备克隆...");

            await _messageService.EditMessageAsync(bot, msg.Chat.Id, statusMessageId, statusBuilder.ToString());

            var itemsForNewSet = sourceSet.Stickers
                .Select(item => new InputSticker(
                    sticker: item.FileId,
                    format: DetermineStickerFormat(item),
                    emojiList: item.Emoji?.Split() ?? new[] { "😊" }
                ))
                .ToList();

            if (!itemsForNewSet.Any())
                throw Oops.Oh("源包中未找到贴纸");

            await bot.CreateNewStickerSet(
                userId: msg.From.Id,
                name: newPackName,
                title: newStickerSetTitle,
                stickers: new[] { itemsForNewSet[0] },
                stickerType: sourceSet.StickerType
            );

            await _messageService.EditMessageAsync(bot, msg.Chat.Id, statusMessageId,
                $"📦 新包创建完成: {newStickerSetTitle}");

            if (itemsForNewSet.Count > 1)
            {
                await _messageService.EditMessageAsync(bot, msg.Chat.Id, statusMessageId,
                    $"📦 正在添加资源...");

                for (int i = 1; i < itemsForNewSet.Count; i++)
                {
                    try
                    {
                        await _messageService.EditMessageAsync(bot, msg.Chat.Id, statusMessageId,
                            $"[进度] 正在添加第 {i}/{itemsForNewSet.Count - 1} 个贴纸");

                        await bot.AddStickerToSet(
                            userId: msg.From.Id,
                            name: newPackName,
                            sticker: itemsForNewSet[i]
                        );
                    }
                    catch (Exception stickerEx)
                    {
                        string errorMsg = $"贴纸 {i} 添加失败: {stickerEx.Message}";
                        stickerErrors.Add(errorMsg);
                        _logger.LogError(stickerEx, $"贴纸 {i} 添加失败 - 用户ID: {msg.From.Id}, 包名: {newPackName}");
                    }

                    await Task.Delay(100);
                }
            }

            var finalMessageBuilder = new StringBuilder()
                .AppendLine("✅ 贴纸包克隆完成！")
                .AppendLine()
                .AppendLine($"📝 标题: {newStickerSetTitle}")
                .AppendLine($"🔢 总计: {itemsForNewSet.Count} 个贴纸")
                .Append($"🔗 链接: https://t.me/add{(sourceSet.StickerType == StickerType.Regular ? "stickers" : "emoji")}/{newPackName}");

            if (stickerErrors.Any())
            {
                finalMessageBuilder
                    .AppendLine()
                    .AppendLine()
                    .AppendLine("⚠️ 部分贴纸上传失败：")
                    .Append(string.Join(Environment.NewLine, stickerErrors));
            }

            await _messageService.EditMessageAsync(bot, msg.Chat.Id, statusMessageId, finalMessageBuilder.ToString());
        }
        catch (Exception ex)
        {
            var errorBuilder = new StringBuilder()
                .AppendLine("❌ 克隆过程中出现错误：")
                .AppendLine(ex.Message)
                .AppendLine()
                .Append("请稍后重试或联系管理员。");

            await _messageService.EditMessageAsync(bot, msg.Chat.Id, statusMessageId, errorBuilder.ToString());
            _logger.LogError(ex, $"克隆贴纸包时发生错误 - 用户ID: {msg.From.Id}, 源包: {sourceStickerSetName}");
        }
    }

    private StickerFormat DetermineStickerFormat(Sticker sticker)
    {
        if (sticker == null)
            throw Oops.Oh("贴纸对象不能为空");

        return sticker.IsVideo ? StickerFormat.Video :
               sticker.IsAnimated ? StickerFormat.Animated :
               StickerFormat.Static;
    }

    private void ValidatePackName(string packName)
    {
        if (string.IsNullOrEmpty(packName))
            throw Oops.Oh("包名称不能为空");

        if (!packName.All(c => char.IsLetterOrDigit(c) || c == '_'))
            throw Oops.Oh("包名称只能包含字母、数字和下划线");
    }

    private string GeneratePackName(string botUsername)
    {
        if (string.IsNullOrEmpty(botUsername))
            throw Oops.Oh("Bot用户名不能为空");

        string randomId = Guid.NewGuid().ToString("N")[..8];
        string packName = $"pack_{randomId}_by_{botUsername}";

        ValidatePackName(packName);
        return packName;
    }
}