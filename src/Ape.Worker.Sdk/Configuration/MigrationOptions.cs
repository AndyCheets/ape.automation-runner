namespace Ape.Worker.Sdk.Configuration;
public sealed record MigrationOptions { public bool Enabled { get; init; } = false; public string ModuleKey { get; init; } = string.Empty; public string ManifestPath { get; init; } = "db/migrations.json"; }
