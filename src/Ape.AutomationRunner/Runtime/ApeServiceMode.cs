namespace Ape.AutomationRunner.Runtime;

public enum ApeServiceMode
{
    Worker,
    Api,
}

public static class ApeServiceModeResolver
{
    public const string EnvironmentVariableName = "APE_SERVICE_MODE";

    public static ApeServiceMode Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ApeServiceMode.Worker;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "worker" => ApeServiceMode.Worker,
            "api" => ApeServiceMode.Api,
            _ => throw new InvalidOperationException(
                $"Unsupported {EnvironmentVariableName} value '{value}'. Supported values are 'worker' and 'api'."
            ),
        };
    }
}
