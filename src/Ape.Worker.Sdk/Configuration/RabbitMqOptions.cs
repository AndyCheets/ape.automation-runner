namespace Ape.Worker.Sdk.Configuration;
public sealed record RabbitMqOptions
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 5672;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string VirtualHost { get; init; } = "/";
    public string CommandExchange { get; init; } = "ape.commands";
    public string EventExchange { get; init; } = "ape.events";
    public string QueueName { get; init; } = string.Empty;
    public string[] BindingKeys { get; init; } = [];
    public ushort PrefetchCount { get; init; } = 10;
}
