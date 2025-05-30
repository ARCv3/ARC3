﻿
using dotenv.net;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using System.Reflection;
using Arc3.Core.Services;
using Arc3.Core.Schema;

using MongoDB.Bson;

namespace Arc3;

internal class Arc3
{
  public static string ArcVersion = "3.8.2";
    
  private DiscordSocketClient? _client;
    
  private IServiceProvider? _serviceProvider;

  private InteractionService? _interactions;

  public static Task Main(string[] args) => new Arc3().MainAsync();

  private async Task MainAsync() {

    // Load the .env file from the root directory change the path if needed
    var envFilePath = Path.GetFullPath("../.env");
    Console.WriteLine($".env file path:{envFilePath}"); // Debugging

    var envOptions = new DotEnvOptions(envFilePaths: new[] { envFilePath });
    DotEnv.Load(envOptions);

    var config = new DiscordSocketConfig {
      GatewayIntents = (Discord.GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent),
      LogLevel = Discord.LogSeverity.Debug,
      TotalShards = int.Parse( Environment.GetEnvironmentVariable("SHARDS") ?? "0") ,
      ShardId = int.Parse( Environment.GetEnvironmentVariable("SHARD_ID") ?? "0")
    };

    // Create a new instance of the socket client
    _client = new DiscordSocketClient(config);
    _interactions = new InteractionService(_client.Rest, new InteractionServiceConfig());

    // Create our service provider
    _serviceProvider = new ServiceCollection()
      .AddSingleton<InteractionService>(_interactions)
      .AddSingleton<DiscordSocketClient>(_client)
      .AddSingleton<DbService>()
      .AddSingleton<KaraokeService>()
      .AddSingleton<PaginationService>()
      .AddSingleton<JailService>()
      .AddSingleton<UptimeService>()
      .AddSingleton<ModMailService>()
      // .AddSingleton<SocketCommService>()
      .BuildServiceProvider();

    // Instantiate your services
    // TODO: Figure out Socket comms
    // var socketComms = _serviceProvider.GetRequiredService<SocketCommService>();
    var dbService = _serviceProvider.GetRequiredService<DbService>();
    var modmailService = _serviceProvider.GetRequiredService<ModMailService>();
    var jailService = _serviceProvider.GetRequiredService<JailService>();
    var karaokeService = _serviceProvider.GetRequiredService<KaraokeService>();
      
    _client.InteractionCreated += async interaction => 
    {
      var ctx = new SocketInteractionContext(_client, interaction);
      await _interactions.ExecuteCommandAsync(ctx, services: _serviceProvider);
    };

    var debug = Environment.GetEnvironmentVariable("DEBUG");

    if (debug == "true") {
      _client.Log += Log;
      _interactions.Log += Log;
    }

    _client.Ready += ReadyAsync;
    _client.JoinedGuild += ClientOnJoinedGuild;
    _client.InteractionCreated += InsightStatTrack;
    
    // Get the token from our environment.
    var token = Environment.GetEnvironmentVariable("TOKEN");


    // Login and start the bot
    await _client.LoginAsync(Discord.TokenType.Bot, token);
    await _client.StartAsync();

    // Block this task until the program is closed!
    await Task.Delay(-1);

  }

  private async Task InsightStatTrack(SocketInteraction arg)
  {
    
    var dbservice = _serviceProvider.GetRequiredService<DbService>();
    
    if (arg.Type == InteractionType.ApplicationCommand)
    {
      var cmd = (SocketSlashCommand)arg;  
      var interaction = cmd.Data;
      
      var data = new BsonDocument();
      foreach (var applicationCommandInteractionDataOption in interaction.Options)
      {
        data.Add(new BsonElement(applicationCommandInteractionDataOption.Name,
          BsonValue.Create(applicationCommandInteractionDataOption.Value.ToString())));
      }

      var stat = new CommandStat()
      {
        Id = Guid.NewGuid().ToString(),
        GuildSnowflake = (long)arg.GuildId!,
        Name = interaction.Name,
        Args = data
      };

      await dbservice.AddAsync<CommandStat>(stat, "Commandstats");
      
    }
  }

  private async Task ClientOnJoinedGuild(SocketGuild arg)
  {
    if (_serviceProvider != null)
    {
      var db = _serviceProvider.GetRequiredService<DbService>();
      var guildinfos = await db.GetItemsAsync<GuildInfo>("Guilds");
      await NewGuild(guildinfos, arg, db);
    }
  }

  private Task Log(LogMessage message)
  {
    Console.WriteLine($"{DateTime.Now} [{message.Severity}] {message.Source}: {message.Message}");
    if (message.Exception is not null) // Check if there is an exception
    {
      // Log the full exception, including the stack trace
      Console.WriteLine($"Exception: {message.Exception.ToString()}");
    }
    return Task.CompletedTask;
  }

  private async Task ReadyAsync()
  {
    
    var debug = Environment.GetEnvironmentVariable("DEBUG");

    if (_client == null)
      throw new Exception("Client is not initialized");

    if (_interactions == null)
      throw new Exception("Interaction service is not initialized");

    try
    { 
      // Things to be run when the bot is ready
      if (_client.Guilds.Any())
      {
        // Register command modules with the InteractionService.
        // Tels it to scan the whole assembly for classes that define slash commands.
        // Also pass in the service provider so services can be accessed in modules
        await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _serviceProvider);

        // Go through all guilds and check if we have a saved infolog

        var db = _serviceProvider.GetRequiredService<DbService>();
        var guildinfos = await db.GetItemsAsync<GuildInfo>("Guilds");
        
        foreach (var guild in _client.Guilds)
        {
          await NewGuild(guildinfos, guild, db);
        }

        if (debug == "true")
        {
          var guildId = ulong.Parse(Environment.GetEnvironmentVariable("GUILD_ID")!);
          await _interactions.RegisterCommandsToGuildAsync(guildId, true);
        }
        else
        {
          await _interactions.RegisterCommandsGloballyAsync(true);
        }
        
      }
      else
      {
        Console.WriteLine($"\nNo guilds found\n");
      }

      Console.WriteLine($"\nLogged in as {_client.CurrentUser.Username}\n" +
                        $"Shard ID: {_client.ShardId}\n" +
                        $"Recommended Shards: {await _client.GetRecommendedShardCountAsync()}\n" +
                        $"Registered {_interactions.SlashCommands.Count} slash commands\n" +
                        $"Bot is a member of {_client.Guilds.Count} guilds\n");

      await _client.SetGameAsync("True Blue", null, ActivityType.Listening);
    }
    catch (Exception e)
    {
      // Log the exception
      Console.WriteLine($"Exception: {e}");
      throw;
    }
  }

  private async Task NewGuild(IEnumerable<GuildInfo> guildinfos, SocketGuild guild, DbService db)
  {
    // Guard if guild info already exists
    if (guildinfos.Any(x => x.GuildSnowflake == (long)guild.Id))
      return;

    // Send the guild info
    await db.AddAsync<GuildInfo>(new GuildInfo()
    {
      GuildSnowflake = (long)guild.Id,
      Premium = false,
      Moderators = new List<string>(),
      OwnerId = (long)guild.OwnerId
    }, "Guilds");

    // TODO: Welcome message!!
    
  }
}