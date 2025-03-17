using Discord.Interactions;
using Discord.WebSocket;

namespace Arc3.Core.Services;

public abstract class ArcService 
{
  
  protected readonly DiscordSocketClient ClientInstance;
  protected readonly InteractionService InteractionService;

  protected ArcService(DiscordSocketClient clientInstance, InteractionService interactionService, string serviceName) 
  {

    ClientInstance = clientInstance;
    InteractionService = interactionService;

    Console.WriteLine("LOADED SERVICE: " + serviceName);
    
  }
}