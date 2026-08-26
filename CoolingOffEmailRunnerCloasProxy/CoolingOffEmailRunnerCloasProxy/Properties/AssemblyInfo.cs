using System.Reflection;
using System.Runtime.InteropServices;

// Nothing in this IIS-hosted assembly ever calls XmlConfigurator.Configure(), so
// without this attribute log4net stays unconfigured and every Log.* call in
// CloasProxyService is silently discarded - "see server logs" points at a file
// that is never written. This wires log4net to the <log4net> section of the
// deployed Web.config on first use.
[assembly: log4net.Config.XmlConfigurator(Watch = true)]

// Legacy projects do not generate assembly metadata at build time the way
// SDK-style projects do, so the attributes are declared explicitly here.
[assembly: AssemblyTitle("CoolingOffEmailRunnerCloasProxy")]
[assembly: AssemblyProduct("CoolingOffEmailRunnerCloasProxy")]
[assembly: AssemblyCompany("CoolingOffEmailRunnerCloasProxy")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("f11ac01f-e766-4e44-88fc-bb50aaa007e2")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
