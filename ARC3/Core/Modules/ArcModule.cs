using Discord.Interactions;
using Discord.WebSocket;
using DnsClient.Protocol;

namespace Arc3.Core.Modules;

public abstract class ArcModule : InteractionModuleBase<SocketInteractionContext> {

  private static readonly Dictionary<string, bool> LoadedDict = new Dictionary<string, bool>();
  protected readonly DiscordSocketClient ClientInstance;

  protected ArcModule(DiscordSocketClient clientInstance, string moduleName) {

    var loaded = LoadedDict.ContainsKey(moduleName);

    ClientInstance = clientInstance;
    
    if (loaded)
      return;

    RegisterListeners(); 

    Console.WriteLine($"MODULE LOADED: {moduleName}");
    LoadedDict[moduleName] = true;
    
  }

  public abstract void RegisterListeners();

}