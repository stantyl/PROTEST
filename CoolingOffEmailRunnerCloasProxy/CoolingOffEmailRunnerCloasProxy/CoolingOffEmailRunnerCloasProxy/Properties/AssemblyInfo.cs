using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Nothing in this IIS-hosted assembly ever calls XmlConfigurator.Configure(), so
// without this attribute log4net stays unconfigured and every Log.* call in
// CloasProxyService is silently discarded - "see server logs" points at a file
// that is never written. This wires log4net to the <log4net> section of the
// deployed Web.config on first use.
[assembly: log4net.Config.XmlConfigurator(Watch = true)]

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("CoolingOffEmailRunnerCloasProxy")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("CoolingOffEmailRunnerCloasProxy")]
[assembly: AssemblyCopyright("Copyright ©  2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("60244a35-ca11-4129-aa44-897bafff51af")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Revision and Build Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
