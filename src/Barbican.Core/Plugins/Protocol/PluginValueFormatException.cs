namespace Barbican.Core.Plugins.Protocol;
public sealed class PluginValueFormatException : FormatException
{
    public PluginValueFormatException(string message) : base(message) { }
    public PluginValueFormatException(string message, Exception innerException) : base(message, innerException) { }
}
