// Test double for the user's preferences: the store the sidebar reads and the editor the tag service writes through, in memory.
using System.Text.Json.Nodes;
using Barbican.Core.Storage;

namespace Barbican.Core.Tests.Plugins.Api;

internal sealed class FakePrefs : IPrefsStore, IPrefsEditor
{
    private JsonObject _values = [];

    public int Puts { get; private set; }

    public PrefsSnapshot Current
    {
        get
        {
            OnRead?.Invoke();
            return PrefsSnapshot.FromJson(_values);
        }
    }

    public event EventHandler? Changed;

    public Action? OnRead { get; set; }

    public void Put(string key, JsonNode? value)
    {
        Puts++;
        var next = (JsonObject)_values.DeepClone();
        next[key] = value?.DeepClone();
        _values = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
}
