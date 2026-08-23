# CoolingOffEmailRunnerCloasProxyConsoleTest

A standalone console tool for testing a deployed
[CoolingOffEmailRunnerCloasProxy](../CoolingOffEmailRunnerCloasProxy) IIS app
end-to-end, without running the full `CoolingOffEmailRunnerConsole` job.

It builds the same CLOAS SOAP request `CoolingOffEmailRunnerConsole`'s
`CloasService` sends (same template, same placeholders), POSTs it to the
proxy's `/CloasService.svc`, and logs what comes back: HTTP status, elapsed
time, the raw response body, and a best-effort parsed summary (`ApiRc`,
`ApiMsg`, and each `PlanCoolingOffDetailsResponse`).

## Configuring test data

All settings live in `App.config` -> `<appSettings>`:

| Key | Purpose |
| --- | --- |
| `CloasProxy.ServiceUrl` | The proxy's own `CloasService.svc` URL to test (not the real CLOAS endpoint). |
| `CloasProxy.TimeoutSeconds` | HTTP timeout for the call. |
| `Cloas.TestPlanIds` | Comma-separated plan IDs to send as test data. |
| `Cloas.SystemId`, `Cloas.UserId`, `Cloas.SystemReference`, `Cloas.SoapAction`, `Cloas.CloasNamespace`, `Cloas.CloasPolicyApiNamespace`, `Cloas.XsiNamespace`, `Cloas.MethodName` | Kept identical in shape to `CoolingOffEmailRunnerConsole`'s `CloasService:*` settings, so the request is representative of what production sends. |
| `Cloas.TemplateFilePath` | Path (relative to the output folder) to the SOAP envelope template. |

Edit these, rebuild if needed, and run the exe - no code changes required to
point at a different proxy environment or a different set of test plan IDs.

## Running

```
CoolingOffEmailRunnerCloasProxyConsoleTest.exe
```

Output goes to the console and to `Logs\CoolingOffEmailRunnerCloasProxyConsoleTest.log`
(log4net rolling file, same format as the other projects in this solution).

Exit codes: `0` success (proxy reachable, SOAP response returned), `1` the
proxy returned a non-success HTTP status, `2` an unexpected error (bad
config, network failure, etc).
