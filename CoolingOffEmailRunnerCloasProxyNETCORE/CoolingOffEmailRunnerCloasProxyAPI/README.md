# CoolingOffEmailRunnerCloasProxyAPI

.NET 8 (ASP.NET Core Web API) re-implementation of
[CoolingOffEmailRunnerCloasProxy](../../CoolingOffEmailRunnerCloasProxy), the
WCF (`.svc`) pass-through proxy that forwards CLOAS SOAP requests from a host
that *can* reach CLOAS.

Same behaviour, different front door:

| | Old WCF proxy | This API |
| --- | --- | --- |
| Its own endpoint | `POST /CloasService.svc` | `POST /api/cloas` |
| What it forwards to | a CLOAS `.svc` URL | **unchanged** - still a CLOAS `.svc` URL |
| Request body | raw SOAP/XML | raw SOAP/XML |
| Headers forwarded | `Content-Type`, `SOAPAction` | `Content-Type`, `SOAPAction` |
| Response | target status + content-type + body, unchanged | target status + content-type + body, unchanged |
| Logging | log4net rolling file, `Logs\` | log4net rolling file, `Logs\` (same pattern) |
| Max request size | 50 MB | 50 MB |

## Configuration

`appsettings.json` -> `CloasProxy`:

| Key | Purpose |
| --- | --- |
| `CloasProxy:TargetServiceUrl` | The real CLOAS `.svc` endpoint every request is forwarded to. |
| `CloasProxy:TimeoutSeconds` | HTTP timeout for the forwarded call (default 300). |

Change `TargetServiceUrl` and restart - no rebuild, and nothing changes for the
caller (it still just points at this proxy).

## Sending a request (XML body, like the console test)

Yes - it takes a raw XML body exactly like
`CoolingOffEmailRunnerCloasProxyConsoleTest` does. POST the SOAP envelope as the
body with `Content-Type: text/xml` and the `SOAPAction` header:

```
POST http://localhost:5214/api/cloas
Content-Type: text/xml; charset=utf-8
SOAPAction: http://ilfs/Cloas/Policy

<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope ...>...</soap:Envelope>
```

See [`CoolingOffEmailRunnerCloasProxyAPI.http`](CoolingOffEmailRunnerCloasProxyAPI.http)
for a full ready-to-run example (open it in Visual Studio / VS Code / Rider and
click **Send Request**), or use `curl`:

```
curl -X POST http://localhost:5214/api/cloas ^
  -H "Content-Type: text/xml; charset=utf-8" ^
  -H "SOAPAction: http://ilfs/Cloas/Policy" ^
  --data-binary "@request.xml"
```

To point the existing `CoolingOffEmailRunnerCloasProxyConsoleTest` at this API,
set its `CloasProxy.ServiceUrl` App.config value to
`http://<host>:<port>/api/cloas`.

### Swagger

Swagger UI is at **`/swagger`** and the OpenAPI document at
`/swagger/v1/swagger.json`, in **every environment**. `POST /api/cloas` gets a
`text/xml` request-body editor (pre-filled with the sample envelope above) and a
`SOAPAction` header field, so you can paste an envelope and hit **Execute**
without leaving the browser.

To turn it off (e.g. in production), set `Swagger:Enabled` to `false` in
`appsettings.json` or as the env var `Swagger__Enabled=false`.

## Running

```
dotnet run
```

> This machine currently has only the ASP.NET Core 10 runtime installed. The
> project targets net8.0 and builds fine; to *run* it either install the
> [ASP.NET Core 8.0 runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
> or set `DOTNET_ROLL_FORWARD=LatestMajor`.

Logs go to the console and to
`Logs\CoolingOffEmailRunnerCloasProxyAPI.log` (log4net rolling file, same format
as the other projects in the solution).
