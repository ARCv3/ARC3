using System.Diagnostics.CodeAnalysis;
using Arc3.Core.Attributes;
using Arc3.Core.Ext;
using Arc3.Core.Schema;
using Arc3.Core.Schema.Ext;
using static Arc3.Core.Schema.Utils.MatchingUtils;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MongoDB.Bson;

namespace Arc3.Core.Services;

public class ModMailService : ArcService
{
    
    private readonly DbService _dbService;

    private readonly List<long> _activeChannelCache = new();
    
    private static readonly string MODMAIL_MODAL_BAN_REASON_CUSTOMID = "modmail.ban.reason";

    public ModMailService(DiscordSocketClient clientInstance, InteractionService interactionService,
        DbService dbService)
        : base(clientInstance, interactionService, "MODMAIL")
    {
        _dbService = dbService;
        clientInstance.MessageReceived += ClientInstanceOnMessageReceived;
        clientInstance.ButtonExecuted += ButtonInteractionCreated;
        clientInstance.SelectMenuExecuted += ClientInstanceOnSelectMenuExecuted;
        clientInstance.ModalSubmitted += ModalInteractionCreated;
        clientInstance.UserIsTyping += ClientInstanceOnUserIsTyping;
        clientInstance.MessageUpdated += ClientInstanceOnMessageUpdated;
        var mails = dbService.GetModMails().GetAwaiter().GetResult();
        
        foreach (var var in mails)
        {
            _activeChannelCache?.Add(var.ChannelSnowflake);
        }
    }

    public async Task ClientInstanceOnMessageReceived(SocketMessage arg)
    {

        /*
         * When msg.EditedTimestamp is not null, this event was triggered by an edited message.
         * We only want new messages to be processed in this case. So we guard for this condition.
         */
        if (arg.EditedTimestamp != null)
        {
            return;
        }

        if (arg.Author.IsBot)
        {
            return;
        }

        await ProcessModmailMessageRecieved(arg);
    }


    public async Task ClientInstanceOnUserIsTyping(Cacheable<IUser, ulong> typingUser, Cacheable<IMessageChannel, ulong> channel)
    {

        if (typingUser.Id == ClientInstance.CurrentUser.Id)
        {
            return;
        }

        // Private messages are handled as from a user
        var allModMails = await _dbService.GetModMails();
        var chan2 = await ClientInstance.GetChannelAsync(channel.Id);

        if ( allModMails.Any( m =>
                MatchingUser(m, typingUser.Value, and: chan2.GetChannelType() == ChannelType.DM) ) )
        {
            await HandleUserTyping(typingUser, allModMails);
            return;
        }

        if ( allModMails.Any( m =>
                MatchingChannel(m, chan2)) )
        {
            await HandleModTyping(allModMails, chan2);
        }

    }

    private async Task ClientInstanceOnMessageUpdated(Cacheable<IMessage, ulong> arg1, SocketMessage arg2, ISocketMessageChannel arg3)
    {

        var msg = await arg1.GetOrDownloadAsync();
        if (msg.Author.IsBot)
            return;

        // Non private messages are handled as from a moderator
        if (arg2.Channel.GetChannelType() != ChannelType.DM)
        {
            await HandleMessageEditFromMod(arg2);
            return;
        }


        await HandleMessageEditFromUser(arg2);
    }

    private async Task ClientInstanceOnSelectMenuExecuted(SocketMessageComponent arg)
    {

        if (!arg.Data.CustomId.StartsWith("modmail.select"))
            return;

        await arg.DeferAsync();
 
        var guildId = arg.Data.Values.First();
        var guild = ClientInstance.GetGuild(ulong.Parse(guildId??"0"));

        var blacklist = await RequireCommandBlacklistAttribute.BlacklistConditionCheck( _dbService, "modmail", guild, arg.User );

        if (blacklist)
        {
            await arg.RespondAsync("You are blacklisted from using modmail", ephemeral: true);
            return;
        }
        
        ModMail? modmail = null;
        try {

            modmail = new ModMail();

            await modmail.InitAsync(ClientInstance, guild, arg.User, _dbService);
            MessageComponent? components = null;
            if (_dbService.Config[guild.Id].ContainsKey("prioritymail"))
            {
                components = new ComponentBuilder().WithButton(
                        "Priority Ping",
                        $"modmail.priority.{modmail.Id}",
                        ButtonStyle.Danger,
                        new Emoji("🚨"))
                    .Build();
            }

            var alert = components == null
                ? ""
                : "If your message is urgent please use the priority ping button below, misuse of this feature will result in blacklisting from modmail and possibly more action taken at the moderation team's discretion.";
            await modmail.SendUserSystem(ClientInstance, $"Your modmail request was recieved! Please wait and a staff member will assist you shortly.\n\n{ alert }", components: components);
            await modmail.SendModMailMenu(ClientInstance);
            
            _activeChannelCache.Add(modmail.ChannelSnowflake);
            await arg.RespondAsync();

        } catch(Exception) {
                
            // TODO: Log Failure 
            // Console.WriteLine(e);
            
            await arg.RespondAsync("Failed to create the modmail session", ephemeral: true);

            if (modmail != null) {
                var modmails = await _dbService.GetModMails();
                if (modmails.Any(x => x.Id == modmail.Id)) 
                    await _dbService.RemoveModMail(modmail.Id);
            }

        }
    }

    public async Task ModalInteractionCreated(SocketModal ctx) {
        
        var eventId = ctx.Data.CustomId;

        if (!eventId.StartsWith("modmail"))
        {
            return;
        }

        await ctx.DeferAsync();

        var eventAction = ClientInstance.GetEventAction(eventId);

        if (eventAction == null)
        {
            return;
        }

        var modmails = await _dbService.GetModMails();
        ModMail modmail;

        try {
            modmail = modmails.First(x => x.Id == eventAction.Value.Item2);
        } catch (InvalidDataException) {
            // TODO: Log failed to get modmail
            return;
        }

        switch (eventAction.Value.Item1) {
            case "modmail.ban.confirm":
                await ConfirmBanUser(modmail, ctx.User, ctx.Data);
                await ctx.RespondAsync("👍🏾", ephemeral: true);
                break;
        }

    }

    private async Task ConfirmBanUser(ModMail modmail, IUser user, SocketModalData data)
    {
        
        IUser member = await modmail.GetUser(ClientInstance);
        String reason = data.Components.First(c => c.CustomId == MODMAIL_MODAL_BAN_REASON_CUSTOMID).Value;

        await LogModmailTranscript(modmail, user);
        await CloseModMailSession(modmail, user);

        await BanMailUser(member, reason, modmail);

    }

    private async Task ButtonInteractionCreated(SocketMessageComponent ctx) {
        
        var eventId = ctx.Data.CustomId;

        if (!eventId.StartsWith("modmail"))
            return;

        await ctx.DeferAsync();

        var eventAction = ClientInstance.GetEventAction(eventId);

        if (eventAction == null)
            return;
        
        await ProcessModmailActions(ctx, eventAction);
    }

    private async Task HandleMessageEditFromUser(SocketMessage arg2)
    {
        // Private messages are handled as from a user
        var mails = await _dbService.GetModMails();

        if (mails.Any(x => (ulong)x.UserSnowflake == arg2.Author.Id))
        {

            // Quit if the message is from a bot
            if (arg2.Author.IsBot)
                return;

            var mail = mails.First(x => (ulong)x.UserSnowflake == arg2.Author.Id);
            await HandleMailChannelEditTranscript(arg2);
            await mail.SendMods(arg2, ClientInstance, _dbService, true);
        }
    }

    private async Task HandleMessageEditFromMod(SocketMessage arg2)
    {
        if (!_activeChannelCache.Contains((long)arg2.Channel.Id)) {
            return;
        }

        // Quit if the message is from a bot
        if (arg2.Author.IsBot)
            return;

        // Handle the mail transcript
        await HandleMailChannelEditTranscript(arg2);

        if (!arg2.Content.StartsWith("#"))
            await HandleMailChannelMessage(arg2, edit: true);

        return;
    }

    private async Task HandleUserTyping(Cacheable<IUser, ulong> user, List<ModMail> mails)
    {
        var mail = mails.First(x => (ulong)x.UserSnowflake == user.Id);
        var chan = await mail.GetChannel(ClientInstance);
        var msgs = chan.GetMessagesAsync(1);

        await msgs.ForEachAwaitAsync(async _ =>
        {
            await chan.TriggerTypingAsync();
        });
    }

    private async Task HandleModTyping(List<ModMail> mails, IChannel chan2)
    {
        var mail = mails.First(x => (ulong)x.ChannelSnowflake == chan2.Id);
        var mailchan = await mail.GetChannel(ClientInstance);
        var usr = await mail.GetUser(clientInstance:ClientInstance);
        var dm = await usr.CreateDMChannelAsync();

        if (dm == null) return;

        if (_dbService.Config.TryGetValue(mailchan.GuildId, out var value)
            && value.TryGetValue("modmailtyping", out var value2) && value2 == "true")
            await dm.TriggerTypingAsync();
    }

    private async Task ProcessModmailActions(SocketMessageComponent ctx, [DisallowNull] (string, string)? eventAction)
    {
        var modmails = await _dbService.GetModMails();
        var modmail = modmails.First(x=> x.Id == eventAction.Value.Item2);

        switch (eventAction.Value.Item1) {

            case "modmail.close":
                await CloseModMailSession(modmail, ctx.User);
                break;

            case "modmail.save":
                await LogModmailTranscript(modmail, ctx.User);
                await CloseModMailSession(modmail, ctx.User);
                break;

            case "modmail.ban":
                await ConfirmBanUser(modmail, ctx);
                break;
            
            case "modmail.ping":
                await modmail.SendUserSystem(ClientInstance, "This is a reminder to check this ticket!");
                await ctx.RespondAsync();
                break;

            case "modmail.priority":
                await HandlePriorityMail(ctx, modmail);
                break;

        }
    }

    private async Task ProcessModmailMessageRecieved(SocketMessage arg)
    {
        // Non private messages are handled as from a moderator
        if (arg.Channel.GetChannelType() != ChannelType.DM)
        {

            if (!_activeChannelCache.Contains((long)arg.Channel.Id)) {
                return;
            }

            await GatherModmailInsights(arg);

            // Quit if the message is commented
            if (arg.Content.StartsWith('#'))
            {
                await HandleMailChannelCommentMessage(arg);
                return;
            }
            
            // Handle the mail message
            await HandleMailChannelMessage(arg);
            return;
        }
        
        
        // Private messages are handled as from a user
        var mails = await _dbService.GetModMails();

        if (mails.All(x => (ulong)x.UserSnowflake != arg.Author.Id)) {

            // If there are no modmails in the database for this user,
            // then we first check if they said modmail
            // if they did then we can start a session if not

            if (!arg.Content.ToLower().Contains("modmail") &&
                !arg.Content.ToLower().Contains("mod") &&
                !arg.Content.ToLower().Contains("mail"))
                return;

            // TODO: Insert server picking mechanism
            // For now choose the default guild

            var selectmenuopts = BuildModmailSelectMenu();

            var content = new ComponentBuilder()
                .WithSelectMenu("modmail.select.server", selectmenuopts);

            await arg.Author.SendMessageAsync("Please select a server to modmail", components:content.Build());

        } else {

            await arg.AddReactionAsync(new Emoji("📤"));

            if (arg.Author.IsBot)
                return;

            var modmail = mails.First(x=>(ulong)x.UserSnowflake == arg.Author.Id);

            if (arg.Content.ToLower().Equals("close session")) {

                await LogModmailTranscript(modmail, arg.Author);
                await CloseModMailSession(modmail, arg.Author);
                return;

            }

            try
            {
                await modmail.SendMods(arg, ClientInstance, _dbService);
            }
            catch (Exception)
            {
                await arg.AddReactionAsync(new Emoji("🔴"));
                await arg.RemoveReactionAsync(new Emoji("📤"), ClientInstance.CurrentUser);
            }
            finally
            {
                await arg.AddReactionAsync(new Emoji("📨"));
                await arg.RemoveReactionAsync(new Emoji("📤"), ClientInstance.CurrentUser);
            }

        }
    }

    private async Task HandlePriorityMail(SocketMessageComponent ctx, ModMail modmail)
    {

        var channel = await modmail.GetChannel(ClientInstance);

        var blacklist = await _dbService.GetItemsAsync<Blacklist>("blacklist");
        if (blacklist.Any(x =>
                x.GuildSnowflake == (long)channel.GuildId && x.UserSnowflake == (long)ctx.User.Id &&
                x.Command is "all" or "prioritymail"))
        {
            await ctx.RespondAsync("You are blacklisted from using priority mail", ephemeral: true);
            return;
        }

        var role = channel.Guild.GetRole(ulong.Parse(_dbService.Config[channel.Guild.Id]["prioritymail"]));
        await channel.SendMessageAsync($"Priority Mail Alert {role.Mention}", allowedMentions: AllowedMentions.All);
        await ctx.RespondAsync();

    }

    private async Task ConfirmBanUser(ModMail mail, SocketMessageComponent ctx) {

        var resp = new ModalBuilder()
            .WithTitle("Are you sure you want to ban this user?")
            .WithCustomId($"modmail.ban.confirm.{mail.Id}")
            .AddTextInput(new TextInputBuilder()
                .WithLabel("Enter a reason for the ban")
                .WithCustomId("modmail.ban.reason")
                .WithPlaceholder("reason")
                .WithRequired(true)
                .WithMaxLength(30))
            .Build();

        await ctx.RespondWithModalAsync(resp);

    }

    private async Task BanMailUser(IUser user, string reason, ModMail mail) {
        var author = ClientInstance.CurrentUser;
        var channel = await mail.GetChannel(ClientInstance);
        var guild = channel.Guild;
        var embed = new EmbedBuilder()
            .WithModMailStyle(ClientInstance)
            .WithAuthor(new EmbedAuthorBuilder()
                .WithName(author.Username)
                .WithIconUrl(author.GetAvatarUrl(format: ImageFormat.Auto)))
                .WithDescription($"You have been banned in {guild.Name} for: ``{reason}``")
                .Build();

        try {
            await user.SendMessageAsync(embed: embed);
        } catch {
            // TODO: Log Failed message send
        } finally {
            await guild.AddBanAsync(user, reason: $"Banned during modmail for: {reason}");
        }

    }

    private async Task GatherModmailInsights(SocketMessage socketMessage)
    {
        var mails = await _dbService.GetModMails();
        var transcripts = await _dbService.GetItemsAsync<Transcript>("transcripts");
        var insights = await _dbService.GetItemsAsync<Insight>("Insights");
        var chan = socketMessage.Channel.Id;
        var guild = ClientInstance.Guilds.First(x => x.Channels.Any(y => y.Id == chan));
        
        ModMail mail;
        try
        {
            mail = mails.First(x => x.ChannelSnowflake == (long)socketMessage.Channel.Id);
        }
        catch (InvalidOperationException)
        {
            // No modmail exists
            // Console.WriteLine($"Failed to get modmail {ex}");
            return;
        }
        
        var msgCount = transcripts.Count(x => x.ModMailId == mail.Id);
        var participants = transcripts.Where(x => x.ModMailId == mail.Id).GroupBy(x => x.UserSnowflake).Count();

        if (insights.Any(x =>
                x.Type == "modmail" && x.Data.Contains("mailid") &&
                x.Data.GetElement("mailid").Value.AsString == mail.Id))
        {

            var insight = insights.First(x =>
                x.Type == "modmail" && x.Data.Contains("mailid") &&
                x.Data.GetElement("mailid").Value.AsString == mail.Id);
            insight.Data.Set("messages", new BsonInt32(msgCount));
            insight.Data.Set("participants", new BsonInt32(participants));
            insight.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            await _dbService.UpdateInsightDataAsync(insight);
        } else if (msgCount > 30 || participants > 5)
        {
            var data = new BsonDocument();
            data.Add("mailid", new BsonString(mail.Id));
            data.Add("messages", new BsonInt32(msgCount));
            data.Add("participants", new BsonInt32(participants));
            data.Add("member", new BsonString(mail.UserSnowflake.ToString()));

            var newInsight = new Insight
            {
                Id = Guid.NewGuid().ToString(),
                Data = data,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                GuildSnowflake = (long)guild.Id,
                Tagline = "Modmail has high activity",
                Type = "modmail",
                Url = $"/{guild.Id}/transcripts/{mail.Id}"
            };

            await _dbService.AddAsync(newInsight, "Insights");
        }

    }

    public List<SelectMenuOptionBuilder> BuildModmailSelectMenu()
    {
        var selectmenuopts = new List<SelectMenuOptionBuilder>();

        foreach (var guild in ClientInstance.Guilds)
        { 
                
            if (!_dbService.Config.ContainsKey(guild.Id))
                continue;
            var guildConfig = _dbService.Config[guild.Id];
                
            if (!guildConfig.ContainsKey("modmailchannel"))
                continue;
                
            // Console.WriteLine(guild.Name);
            IEmote emoji = guild.Emotes.FirstOrDefault<IEmote>(x => x.Name == "arc_icon", new Emoji("🌐"));
            selectmenuopts.Add(new SelectMenuOptionBuilder
            {
                Description = guild.Description?[..90] + "...",
                Emote = emoji,
                IsDefault = false,
                Label = guild.Name,
                Value = guild.Id.ToString()
            });
        }

        return selectmenuopts;
    }

    private async Task CloseModMailSession(ModMail m, IUser user)
    {
        _activeChannelCache.Remove(m.ChannelSnowflake);
        await m.SendUserSystem(ClientInstance, $"Your mod mail session was closed by {user.Mention}!");
        await m.CloseAsync(ClientInstance, _dbService);
    }

    private async Task LogModmailTranscript(ModMail m, IUser s) {

        var hostedUrl = Environment.GetEnvironmentVariable("HOSTED_URL");
        // await m.SaveTranscriptAsync(_clientInstance, _dbService);
        var channel = await m.GetChannel(ClientInstance);
        var guild = channel.Guild;
        var transcriptchannel = await ClientInstance.GetChannelAsync(ulong.Parse(_dbService.Config[guild.Id]["transcriptchannel"]));
        var user = await m.GetUser(ClientInstance);
        var embed = new EmbedBuilder()
            .WithModMailStyle(ClientInstance)
            .WithTitle("Modmail Transcript")
            .WithDescription($"**Modmail with:** {user.Mention}\n**Saved** <t:{DateTimeOffset.Now.ToUnixTimeSeconds()}:R> **by** {s.Mention}\n\n[Transcript]({hostedUrl}/{guild.Id}/transcripts/{m.Id})")
            .Build();

        await ((SocketTextChannel)transcriptchannel).SendMessageAsync(embed: embed);

    }
    
    private async Task HandleMailChannelCommentMessage(SocketMessage msg)
    {
        
        var mails = await _dbService.GetModMails();
        ModMail mail;
        try
        {
            mail = mails.First(x => x.ChannelSnowflake == (long)msg.Channel.Id);

            var channel = await mail.GetChannel(ClientInstance);
                        
            var transcript = new Transcript {
                Id = msg.Id.ToString(),
                ModMailId = mail.Id,
                UserSnowflake = (long)msg.Author.Id,
                AttachmentURls = msg.Attachments.Select(x => x.ProxyUrl).ToArray(),
                CreatedAt = msg.CreatedAt.UtcDateTime,
                GuildSnowflake = (long)channel.Guild.Id,
                MessageContent = msg.Content,
                TranscriptType = "Modmail",
                Comment = true
            };

            await _dbService.AddTranscriptAsync(transcript);

        }
        catch (InvalidOperationException ex)
        {
            // No modmail exists
            await Console.Error.WriteAsync($"Failed to get modmail {ex}");
        }

    }

    private async Task HandleMailChannelMessage(SocketMessage msg, bool edit = false)
    {
    
        await msg.AddReactionAsync(new Emoji("📤"));
        
        var mails = await _dbService.GetModMails();
        ModMail mail;
        try
        {
            mail = mails.First(x => x.ChannelSnowflake == (long)msg.Channel.Id);
        }
        catch (InvalidOperationException ex)
        {
            // No modmail exists
            await Console.Error.WriteAsync($"Failed to get modmail {ex}");
            return;
        }

        await mail.SendUserAsync(msg, ClientInstance, _dbService, edit);
    }

    private async Task HandleMailChannelEditTranscript(SocketMessage msg)
    {
    
        await msg.AddReactionAsync(new Emoji("✏️"));

        try
        {
            await _dbService.UpdateTranscriptAsync(msg);
        }
        catch (Exception ex)
        {
            // No modmail exists
            // Console.WriteLine($"Failed to get modmail {ex}");
            await msg.RemoveReactionAsync(new Emoji("✏️"), ClientInstance.CurrentUser);
            await msg.AddReactionAsync(new Emoji("🟡"));
        }
        
    }
    
}