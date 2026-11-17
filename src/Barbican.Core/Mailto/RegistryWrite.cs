// Mirrors: Rust mailto.rs:181 windows_plan; Windows default-programs registration (HKCU\Software\Classes, Clients\Mail, RegisteredApplications). Contract from the M3 plan section 4.5; owner composer-ui.

namespace Barbican.Core.Mailto;

/// <summary>One registry value the default-mail-app registration writes (planned and tested here, executed only with the user's consent).</summary>
public sealed record RegistryWrite(string Key, string Name, string Value);
