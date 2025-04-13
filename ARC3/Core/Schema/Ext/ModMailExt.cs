using Arc3.Core.Ext;
using Arc3.Core.Services;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;

namespace Arc3.Core.Schema.Ext;

public static class ModMailExt
{
    public static async Task<IUser> GetUser(this ModMail self, DiscordSocketClient clientInstance)
    {
        var user = await clientInstance.GetUserAsync((ulong)self.UserSnowflake);
        return user;
    }

    public static async Task<ITextChannel> GetChannel(this ModMail self, DiscordSocketClient clientInstance) {
        var channel = await clientInstance.GetChannelAsync((ulong)self.ChannelSnowflake);
        return (ITextChannel)channel;
    }   

    public static async Task<IWebhook> GetWebhook(this ModMail self, DiscordSocketClient clientInstance)
    {
        var  channel = await clientInstance.GetChannelAsync((ulong)self.ChannelSnowflake);
        var interactionchannel = (IIntegrationChannel)channel;

        var webhook = await interactionchannel.GetWebhookAsync((ulong)self.WebhookSnowflake);
        return webhook;
    }
    
    public static async Task SendUserAsync(this ModMail self, SocketMessage msg, DiscordSocketClient clientInstance, DbService dbService, bool edit = false)
    {
        
        var embed = new EmbedBuilder()
            .WithModMailStyle(clientInstance)
            .WithAuthor(msg.Author.Username, msg.Author.GetDisplayAvatarUrl())
            .WithDescription(edit? "EDIT: " + msg.Content : msg.Content)
            .Build();

        var channel = await self.GetChannel(clientInstance);

        if (!edit)
        {
            await SaveTranscript(self, msg, dbService, channel, "Modmail");
        }
        
        var attLen = msg.Attachments.Count;
        // Send the message
        if (!string.IsNullOrWhiteSpace(msg.Content) && attLen < 0)
        {
            try
            {
                var user = await self.GetUser(clientInstance);
                await user.SendMessageAsync(embed: embed);
            }
            catch (Exception)
            {
                
                await msg.AddReactionAsync(new Emoji("🔴"));
                
                await msg.RemoveReactionAsync(new Emoji("📤"), clientInstance.CurrentUser);
            }
            finally
            {
                
                await msg.AddReactionAsync(new Emoji("📨"));
                
                await msg.RemoveReactionAsync(new Emoji("📤"), clientInstance.CurrentUser);
                
            }
            
        }
            
        
        // Share attachments
        if (msg.Attachments.Count > 0)
        {

            var attCount = 0;

            foreach (var attachment in msg.Attachments)
            {
                var msgContent = attCount == 0? msg.Content : "Image: ";
                var emb = new EmbedBuilder()
                    .WithModMailStyle(clientInstance)
                    .WithAuthor(msg.Author.Username, msg.Author.GetDisplayAvatarUrl())
                    .WithDescription(string.IsNullOrWhiteSpace(msg.Content)? "Image: " : msgContent)
                    .WithImageUrl(attachment.Url)
                    .Build();
                
                try
                {
                    var user = await self.GetUser(clientInstance);
                    await user.SendMessageAsync(embed: emb);
                }
                catch (Exception)
                {
                    await msg.AddReactionAsync(new Emoji("🔴"));
                    await msg.RemoveReactionAsync(new Emoji("📤"), clientInstance.CurrentUser);
                }
                finally
                {
                    await msg.AddReactionAsync(new Emoji("📨"));
                    await msg.RemoveReactionAsync(new Emoji("📤"), clientInstance.CurrentUser);
                }
                attCount++;
            }
        }
        
    }

    public static async Task SendUserSystem(this ModMail self, DiscordSocketClient clientInstance, string content, MessageComponent? components = null)
    {
        
        var author = clientInstance.CurrentUser;

        var embed = new EmbedBuilder()
            .WithModMailStyle(clientInstance)
            .WithAuthor(author.Username, author.GetDisplayAvatarUrl())
            .WithDescription(content)
            .Build();

        var user = await self.GetUser(clientInstance);

        if (components is null)
            await user.SendMessageAsync(embed: embed);
        else
            await user.SendMessageAsync(embed: embed, components: components);
        
    }

    public static async Task SendModsAttachment(this ModMail self, Attachment attachment, DiscordSocketClient clientInstance, SocketMessage msg)
    {
        DiscordWebhookClient client = new DiscordWebhookClient(await self.GetWebhook(clientInstance));
        await client.SendMessageAsync(attachment.ProxyUrl, avatarUrl: msg.Author.GetDisplayAvatarUrl());
    }

    public static async Task SendMods(this ModMail self, SocketMessage msg, DiscordSocketClient clientInstance, DbService dbService, bool edit = false)
    {
   
        DiscordWebhookClient client = new DiscordWebhookClient(await self.GetWebhook(clientInstance));

        // First, check if there are attachements on the message
        if (msg.Attachments.Count > 0 && !edit)
        {
            // Send attachements
            foreach (var attachment in msg.Attachments)
            {
                await self.SendModsAttachment(attachment, clientInstance, msg);
            }

            if (msg.Content == "")
            {
                return;
            }

        }

        await client.SendMessageAsync(edit? "EDIT: " + msg.CleanContent : msg.CleanContent, avatarUrl: msg.Author.GetDisplayAvatarUrl());
        
        var channel = await self.GetChannel(clientInstance);

        if (!edit)
        {
            await SaveTranscript(self, msg, dbService, channel, "Modmail");
        }


    }

    private static async Task SaveTranscript(ModMail self, SocketMessage msg, DbService dbService, ITextChannel channel, String type)
    {
        var transcript = new Transcript
        {
            Id = msg.Id.ToString(),
            ModMailId = self.Id,
            UserSnowflake = (long)msg.Author.Id,
            AttachmentURls = msg.Attachments.Select(x => x.ProxyUrl).ToArray(),
            CreatedAt = msg.CreatedAt.UtcDateTime,
            GuildSnowflake = (long)channel.Guild.Id,
            MessageContent = msg.Content,
            TranscriptType = type
        };

        await dbService.AddTranscriptAsync(transcript);
    }

    public static async Task SendModMailMenu(this ModMail self, DiscordSocketClient clientInstance, Appeal? appeal = null)
    {

        var user = await self.GetUser(clientInstance);
        var channel = await self.GetChannel(clientInstance);

        var embed = appeal == null? 
            new EmbedBuilder()
                .WithModMailStyle(clientInstance)
                .WithTitle("Modmail")
                .WithDescription($"A Modmail session was opened by {user.Mention}")
                .Build()
            :
            new EmbedBuilder()
                .WithModMailStyle(clientInstance)
                .WithTitle("Modmail")
                .WithDescription($"Modmail for {user.Mention}'s ban appeal")
                .Build();
        
        var buttons = appeal == null?
            new ComponentBuilder()
                .WithRows(new List<ActionRowBuilder> {
                    new ActionRowBuilder()
                        .WithButton("Close", 
                                    $"modmail.save.{self.Id}",
                                    ButtonStyle.Secondary,
                                    new Emoji("📝"))
                        .WithButton("Ban",
                                    $"modmail.ban.{self.Id}",
                                    ButtonStyle.Danger,
                                    new Emoji("🔨"))
                        .WithButton("Ping",
                                $"modmail.ping.{self.Id}",
                                    ButtonStyle.Success,
                                    new Emoji("📣"))
                })
                .Build()
            :
            new ComponentBuilder()
                .WithRows(new List<ActionRowBuilder> {
                    new ActionRowBuilder()
                        .WithButton("Save and Close", 
                                    $"modmail.save.{self.Id}",
                                    ButtonStyle.Secondary,
                                    new Emoji("📝"))
                        .WithButton("Close",
                                    $"modmail.close.{self.Id}",
                                    ButtonStyle.Danger,
                                    new Emoji("🔒"))
                        // TODO: Add Unban and Deny buttons
                        // .WithButton("Ban",
                        //             $"modmail.ban.{self.Id}",
                        //             ButtonStyle.Danger,
                        //             new Emoji("🔨"))
                })
                .Build();
        
        await channel.SendMessageAsync(embed:embed, components: buttons);
    }

    public static async Task<bool> InitAsync(this ModMail self, DiscordSocketClient clientInstance, SocketGuild guild, SocketUser user, DbService dbService) {

        self.UserSnowflake = (long)user.Id;

        if (guild is null)
            return false;

        var guildConfig = dbService.Config[guild.Id];
        var modmails = await dbService.GetModMails();
        
        if (modmails.Any(x => x.UserSnowflake == self.UserSnowflake))
            return false;

        if (!guildConfig.ContainsKey("modmailchannel"))
            return false;
        
        var modmailChannelSnowflake = ulong.Parse(guildConfig["modmailchannel"]);
        var mailCategory = guild.GetCategoryChannel(modmailChannelSnowflake);

        if (!(mailCategory.GetChannelType() == ChannelType.Category))
            return false;
        
        var mailChannel = await guild.CreateTextChannelAsync(
            $"Modmail-{user.Username}",
            x => {
                x.ChannelType = ChannelType.Text;
                x.CategoryId = mailCategory.Id;
            }
        );

        var webhook = await mailChannel.CreateWebhookAsync(user.Username);

        self.Id = Guid.NewGuid().ToString();
        self.ChannelSnowflake = (long)mailChannel.Id;
        self.WebhookSnowflake = (long)webhook.Id;

        await dbService.AddModMail(self);

        return true;
        
    }

    public static async Task CloseAsync(this ModMail self, DiscordSocketClient client, DbService dbService) {
        await dbService.RemoveModMail(self.Id);
        var channel = await self.GetChannel(client);
        await channel.DeleteAsync();
    }

}