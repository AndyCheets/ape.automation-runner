namespace Ape.Worker.Sdk.Configuration;
public sealed record ServiceIdentityOptions
{
    public string ServiceName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}
