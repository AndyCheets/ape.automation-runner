namespace Ape.Worker.Sdk.Configuration;

public sealed record TenantResolutionOptions
{
    public bool Enabled { get; init; } = false;
    public string DefaultTenantKey { get; init; } = "default";
}
