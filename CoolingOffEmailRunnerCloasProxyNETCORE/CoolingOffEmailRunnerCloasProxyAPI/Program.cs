using System.Reflection;
using CoolingOffEmailRunnerCloasProxyAPI;
using CoolingOffEmailRunnerCloasProxyAPI.Controllers;
using CoolingOffEmailRunnerCloasProxyAPI.Swagger;
using log4net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// log4net - same rolling-file appender/pattern as the rest of the solution.
var log4NetConfig = new FileInfo(Path.Combine(AppContext.BaseDirectory, "log4net.config"));
log4net.Config.XmlConfigurator.ConfigureAndWatch(
    LogManager.GetRepository(Assembly.GetEntryAssembly()!), log4NetConfig);

// CloasProxy:* settings (target .svc URL, timeout).
builder.Services.Configure<CloasProxyOptions>(
    builder.Configuration.GetSection(CloasProxyOptions.SectionName));
var proxyOptions = builder.Configuration.GetSection(CloasProxyOptions.SectionName)
    .Get<CloasProxyOptions>() ?? new CloasProxyOptions();

// One pooled HttpClient for the forwarded calls, timeout from config.
builder.Services.AddHttpClient(CloasProxyController.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(proxyOptions.TimeoutSeconds);
});

// Match the 50 MB request headroom the WCF proxy allowed (Web.config
// maxAllowedContentLength / maxReceivedMessageSize = 52428800).
builder.Services.Configure<KestrelServerOptions>(o =>
    o.Limits.MaxRequestBodySize = 52_428_800);
builder.Services.Configure<IISServerOptions>(o =>
    o.MaxRequestBodySize = 52_428_800);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CoolingOffEmailRunnerCloasProxyAPI",
        Version = "v1",
        Description =
            "Pass-through proxy for CLOAS. POST a raw CLOAS SOAP/XML envelope " +
            "(Content-Type: text/xml, SOAPAction header) to /api/cloas; it is " +
            "forwarded unchanged to the configured CLOAS .svc target and the " +
            "target's response is streamed straight back."
    });

    // Surface the controller/action <summary> docs in the Swagger UI.
    var xmlDoc = Path.Combine(AppContext.BaseDirectory,
        $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlDoc))
    {
        c.IncludeXmlComments(xmlDoc);
    }

    // Give POST /api/cloas a text/xml body editor (it reads the raw stream, so
    // Swashbuckle would otherwise show no request body).
    c.OperationFilter<RawXmlBodyOperationFilter>();
});

var app = builder.Build();

// Swagger is enabled in every environment; set Swagger:Enabled=false to turn it off.
if (app.Configuration.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CoolingOffEmailRunnerCloasProxyAPI v1");
        c.DocumentTitle = "CoolingOffEmailRunnerCloasProxyAPI";
    });
}

app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => Results.Content(
    "CoolingOffEmailRunnerCloasProxyAPI is running.\n" +
    "POST your CLOAS SOAP/XML request (Content-Type: text/xml, SOAPAction header) to /api/cloas.\n" +
    "Swagger UI: /swagger",
    "text/plain"));

app.Run();
