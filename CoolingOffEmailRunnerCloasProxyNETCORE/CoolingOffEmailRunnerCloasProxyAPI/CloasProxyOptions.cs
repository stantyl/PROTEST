namespace CoolingOffEmailRunnerCloasProxyAPI;

public sealed class CloasProxyOptions
{
    public const string SectionName = "CloasProxy";

    public string? TargetServiceUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 300;
}
