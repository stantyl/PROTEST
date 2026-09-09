namespace CoolingOffEmailRunnerCloasProxyAPI;

/// <summary>
/// Bound from the <c>CloasProxy</c> section of appsettings.json - the .NET 8 API
/// equivalent of the old WCF proxy's <c>CloasProxy.*</c> Web.config appSettings.
/// </summary>
public sealed class CloasProxyOptions
{
    public const string SectionName = "CloasProxy";

    /// <summary>
    /// The real CLOAS service every request is forwarded to. This is still a
    /// classic <c>.svc</c> SOAP endpoint - only this proxy's own front door
    /// changed from <c>.svc</c> to a plain HTTP API route.
    /// </summary>
    public string? TargetServiceUrl { get; set; }

    /// <summary>HTTP timeout, in seconds, for the forwarded call. Defaults to 300.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}
