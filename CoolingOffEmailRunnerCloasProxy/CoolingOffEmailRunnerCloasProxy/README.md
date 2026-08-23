# CoolingOffEmailRunnerCloasProxy

A thin reverse proxy for the CLOAS SOAP service (`CloasService.svc`), for
environments where `CoolingOffEmailRunnerConsole` cannot reach CLOAS directly
because of an internal IP restriction.

It is deployed on a server that **does** have network access to CLOAS. It
exposes its own `/CloasService.svc`, which forwards every request byte-for-byte
to the real CLOAS URL and returns the response byte-for-byte, unmodified. It
does not parse, validate, or re-shape the SOAP envelope in either direction -
`CoolingOffEmailRunnerConsole`'s `CloasService` (see
`CoolingOffEmailRunnerConsole/Services/CloasService.cs`) keeps building and
parsing the same SOAP request/response it always has, unaware it's talking to
a proxy.

## How it works

- `CloasService.svc` activates `CloasProxyService` (`CloasProxyService.svc.cs`)
  via WCF's `webHttpBinding`, bound with `BodyStyle = Bare` so both the
  request and response are raw, untouched byte streams.
- On each call, `CloasProxyService.Process` reads the incoming body and the
  `SOAPAction`/`Content-Type` headers, POSTs them unchanged to
  `CloasProxy.TargetServiceUrl` (`Web.config` -> `appSettings`), and copies the
  downstream status code, content type and body straight back to the caller.
- Every forwarded request/response (size, status, elapsed time) and any error
  is logged with log4net to `Logs\CoolingOffEmailRunnerCloasProxy.log`, in the
  same rolling-file format used by `CoolingOffEmailRunnerConsole`.

## Swapping the target URL

Change `CloasProxy.TargetServiceUrl` in `Web.config` and recycle the app pool.
No rebuild, no redeploy, no change to `CoolingOffEmailRunnerConsole`.

## Deploying to IIS

This is a plain class library project (not an ASP.NET "Web Application"
project), so there is no web publish pipeline to run. Deploy by copying files
into the IIS site/application's physical folder:

1. On the target server, enable **.NET Framework 4.5 Advanced Services ->
   WCF Services -> HTTP Activation** (Windows Features / Server Manager).
2. Create an IIS site or application pointing at an empty folder, running
   under a .NET Framework 4.x app pool (Integrated pipeline).
3. Copy into that folder:
   - `Web.config`
   - `CloasService.svc`
   - the `bin\` build output (`CoolingOffEmailRunnerCloasProxy.dll`,
     `log4net.dll`, and the rest of `bin\<Configuration>\net452\`) into the
     site's `bin\` subfolder.
4. Edit `CloasProxy.TargetServiceUrl` in the deployed `Web.config` to the real
   CLOAS URL reachable from that server (e.g. what is currently
   `CloasService:ServiceUrl` in `CoolingOffEmailRunnerConsole`'s
   `appsettings.*.json`).
5. Browse `http://<this-server>/CloasService.svc` - route 404s vs. 200s at
   that point are IIS routing issues; correctness of the forwarded call needs
   a real POST (e.g. from `CoolingOffEmailRunnerConsole` itself).

## Pointing CoolingOffEmailRunnerConsole at the proxy

In `CoolingOffEmailRunnerConsole/appsettings.<Environment>.json`, change:

```json
"CloasService": {
  "ServiceUrl": "http://<this-proxy-server>/CloasService.svc"
}
```

Nothing else in `CoolingOffEmailRunnerConsole` needs to change - it still
builds the same SOAP envelope and parses the same SOAP response; only the
host it sends that envelope to is different.
