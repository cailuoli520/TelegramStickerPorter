namespace TelegramStickerPorter;

public class StickerService
{
    private readonly ILogger<StickerService> _logger;
    private readonly MessageService _messageService;
    private readonly HttpClient _httpClient;

    public StickerService(ILogger<StickerService> logger, MessageService messageService)
    {
        _logger = logger;
        _messageService = messageService;
        _httpClient = new HttpClient(); // 用于下载贴纸文件的HttpClient实例
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

    /// <summary>
    /// 发送贴纸包下载使用说明
    /// </summary>
    public async Task SendDownloadInstructionsAsync(Bot bot, Telegram.Bot.Types.Message msg)
    {
        var messageText = new StringBuilder()
            .AppendLine("💾 <b>贴纸包下载使用说明</b> 💾")
            .AppendLine()
            .AppendLine("您可以下载指定贴纸包中的所有贴纸到本地设备。使用方法如下：")
            .AppendLine()
            .AppendLine("<code>下载#目标文件夹路径#贴纸包链接</code>")
            .AppendLine()
            .AppendLine("例如：")
            .AppendLine("<code>下载#./Downloads/my-stickers#https://t.me/addstickers/animals_collection</code>")
            .AppendLine()
            .AppendLine("🔹 <b>下载</b>：命令前缀，触发下载操作")
            .AppendLine("🔹 <b>目标文件夹路径</b>：存放下载贴纸的本地文件夹路径")
            .AppendLine("🔹 <b>贴纸包链接</b>：要下载的Telegram贴纸包链接")
            .AppendLine()
            .AppendLine("注意：")
            .AppendLine("- 请确保有足够的磁盘空间存储贴纸文件")
            .AppendLine("- 大型贴纸包可能需要较长的下载时间")
            .AppendLine("- 建议在网络良好的环境下进行操作")
            .ToString();

        await _messageService.SendMessageAsync(bot, msg.Chat.Id, messageText, replyParameters: msg);
    }

    /// <summary>
    /// 处理贴纸包下载命令
    /// </summary>
    public async Task HandleDownloadCommandAsync(Bot bot, Telegram.Bot.Types.Message msg)
    {
        try
        {
            string[] parts = msg.Text.Split('#');

            if (parts.Length != 3)
            {
                var errorMsg = new StringBuilder()
                    .AppendLine("格式错误！请使用正确的格式：")
                    .Append("下载#目标文件夹路径#贴纸包链接")
                    .ToString();

                await _messageService.SendMessageAsync(bot, msg.Chat.Id, errorMsg, replyParameters: msg);
                return;
            }

            string downloadDirectory = parts[1].Trim();
            string stickerUrl = parts[2];

            if (!stickerUrl.StartsWith("https://t.me/add"))
            {
                await _messageService.SendMessageAsync(bot, msg.Chat.Id,
                    "贴纸链接格式错误！链接应该以 https://t.me/add 开头",
                    replyParameters: msg);
                return;
            }

            string stickerSetName = stickerUrl
                .Replace("https://t.me/addstickers/", "")
                .Replace("https://t.me/addemoji/", "");

            var statusMessage = $"💾 正在准备下载贴纸包，请稍候...\n此过程可能需要几分钟，取决于贴纸数量和大小。";
            var statusMessageId = await _messageService.SendMessageAsync(bot, msg.Chat.Id, statusMessage);

            _ = Task.Run(async () => await ProcessDownloadStickerTaskAsync(
                bot, msg, statusMessageId, stickerSetName, downloadDirectory));
        }
        catch (Exception ex)
        {
            var errorBuilder = $"[错误] 发生异常: {ex.Message}";
            _logger.LogError(ex, "处理下载命令时发生异常");
            await _messageService.SendMessageAsync(bot, msg.Chat.Id, errorBuilder);
        }
    }

    /// <summary>
    /// 异步处理贴纸包下载任务
    /// </summary>
    private async Task ProcessDownloadStickerTaskAsync(
        Bot bot,
        Telegram.Bot.Types.Message msg,
        int statusMessageId,
        string stickerSetName,
        string downloadDirectory)
    {
        List<(string fileName, Exception error)> downloadErrors = new List<(string, Exception)>();
        
        try
        {
            // 创建下载目录
            Directory.CreateDirectory(downloadDirectory);

            // 获取贴纸包信息
            var stickerSet = await bot.GetStickerSet(stickerSetName);
            
            var statusBuilder = new StringBuilder()
                .AppendLine("📦 贴纸包信息:")
                .AppendLine($"标题: {stickerSet.Title}")
                .AppendLine($"贴纸数量: {stickerSet.Stickers.Length}")
                .AppendLine($"类型: {stickerSet.StickerType}")
                .AppendLine($"下载位置: {Path.GetFullPath(downloadDirectory)}")
                .AppendLine()
                .Append("⬇️ 开始下载贴纸...");

            await _messageService.EditMessageAsync(bot, msg.Chat.Id, statusMessageId, statusBuilder.ToString());

            int downloadedCount = 0;
            int totalCount = stickerSet.Stickers.Length;

            // 逐个下载贴纸
            for (int i = 0; i < totalCount; i++)
            {
                var sticker = stickerSet.Stickers[i];
                
                try
                {
                    // 更新进度状态
                    await _messageService.EditMessageAsync(bot, msg.Chat.Id, statusMessageId,
                        $"[进度] 正在下载第 {i + 1}/{totalCount} 个贴纸\n已完成: {downloadedCount}/{totalCount}");

                    // 获取贴纸文件信息
                    var fileInfo = await bot.GetFile(sticker.FileId);
                    
                    if (fileInfo == null)
                    {
                        downloadErrors.Add((GetFileName(i, sticker), new Exception("无法获取文件信息")));
                        continue;
                    }

                    // 确定文件扩展名
                    string extension = GetStickerExtension(fileInfo);
                    string fileName = $"{stickerSet.Name}_{i:D3}{extension}";
                    string filePath = Path.Combine(downloadDirectory, fileName);

                    // 下载文件
                    await DownloadStickerFileAsync(bot, fileInfo.FilePath, filePath);
                    
                    downloadedCount++;
                    
                    // 短暂延迟以避免请求过于频繁
                    await Task.Delay(200);
                }
                catch (Exception ex)
                {
                    downloadErrors.Add((GetFileName(i, sticker), ex));
                    _logger.LogWarning(ex, "下载贴纸失败: {Index}/{Total}", i + 1, totalCount);
                }
            }

            // 生成最终结果消息
            var finalMessageBuilder = new StringBuilder()
                .AppendLine("✅ 贴纸包下载完成！")
                .AppendLine()
                .AppendLine($"📁 下载位置: {Path.GetFullPath(downloadDirectory)}")
                .AppendLine($"📝 贴纸包标题: {stickerSet.Title}")
                .AppendLine($"🔢 总数: {totalCount} 个贴纸")
                .AppendLine($"✅ 成功下载: {downloadedCount} 个")
                .AppendLine($"❌ 失败: {downloadErrors.Count} 个");

            if (downloadErrors.Any())
            {
                finalMessageBuilder.AppendLine().AppendLine("⚠️ 失败的贴纸:");
                foreach (var (fileName, error) in downloadErrors.Take(5)) // 只显示前5个错误
                {
                    finalMessageBuilder.AppendLine($"- {fileName}: {error.Message}");
                }
                if (downloadErrors.Count > 5)
                {
                    finalMessageBuilder.AppendLine($"- ...还有 {downloadErrors.Count - 5} 个错误");
                }
            }

            await _messageService.EditMessageAsync(bot, msg.Chat.Id, statusMessageId, finalMessageBuilder.ToString());
        }
        catch (Exception ex)
        {
            var errorBuilder = new StringBuilder()
                .AppendLine("❌ 下载过程中出现错误：")
                .AppendLine(ex.Message)
                .AppendLine()
                .Append("请检查网络连接和目标文件夹权限后重试。");

            await _messageService.EditMessageAsync(bot, msg.Chat.Id, statusMessageId, errorBuilder.ToString());
            _logger.LogError(ex, $"下载贴纸包时发生错误 - 用户ID: {msg.From.Id}, 贴纸包: {stickerSetName}");
        }
    }

    /// <summary>
    /// 下载单个贴纸文件
    /// </summary>
    private async Task DownloadStickerFileAsync(Bot bot, string filePath, string destinationPath)
    {
        try
        {
            // 使用Telegram Bot API下载文件
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            await bot.DownloadFile(filePath, fileStream);
        }
        catch (Exception ex)
        {
            throw new Exception($"下载文件失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 根据文件信息获取贴纸文件扩展名
    /// </summary>
    private string GetStickerExtension(WTelegram.Types.File fileInfo)
    {
        if (fileInfo.MimeType?.Contains("video") == true)
            return ".mp4";
        else if (fileInfo.MimeType?.Contains("animated") == true)
            return ".tgs"; // Telegram动画贴纸格式
        else
            return ".png"; // 静态贴纸默认使用PNG格式
    }

    /// <summary>
    /// 生成文件名
    /// </summary>
    private string GetFileName(int index, Sticker sticker)
    {
        return $"sticker_{index:D3}{(sticker.Emoji != null ? "_" + sticker.Emoji.Replace(" ", "") : "")}";
    }
}