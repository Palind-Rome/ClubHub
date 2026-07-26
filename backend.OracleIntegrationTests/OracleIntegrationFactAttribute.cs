namespace ClubHub.Api.OracleIntegrationTests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class OracleIntegrationFactAttribute : FactAttribute
{
    public OracleIntegrationFactAttribute()
    {
        if (!OracleIntegrationEnvironment.IsEnabled)
        {
            Skip =
                "Set CLUBHUB_ORACLE_INTEGRATION_CONNECTION and " +
                "CLUBHUB_ORACLE_INTEGRATION_ISOLATED=true to run against an isolated Oracle schema.";
        }
    }
}

internal static class OracleIntegrationEnvironment
{
    private const string ConnectionVariable = "CLUBHUB_ORACLE_INTEGRATION_CONNECTION";
    private const string IsolationVariable = "CLUBHUB_ORACLE_INTEGRATION_ISOLATED";

    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)) &&
        string.Equals(
            Environment.GetEnvironmentVariable(IsolationVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public static string ConnectionString =>
        IsEnabled
            ? Environment.GetEnvironmentVariable(ConnectionVariable)!
            : throw new InvalidOperationException(
                "Oracle integration tests require an isolated schema and explicit safety confirmation.");
}
