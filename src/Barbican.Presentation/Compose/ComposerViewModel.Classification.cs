// Classification picker state is local to the open composer; the send pipeline revalidates policy.
using Barbican.Core.Compose;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Presentation.Compose;

public sealed partial class ComposerViewModel
{
    private ComposerClassificationChoice? _classificationChoice;
    private ClassificationPolicy? _classificationPolicy;
    private bool _classificationReady;
    private object? _classificationOwner;
    private string? _classificationIdentity;

    /// <summary>Null means the current policy default; an explicit null label means no classification.</summary>
    public ComposerClassificationChoice? ClassificationChoice
    {
        get => _classificationChoice;
        set => SetProperty(ref _classificationChoice, value);
    }

    /// <summary>The actual From address used when building this draft.</summary>
    public string? ClassificationSender => EffectiveFrom()?.Email;

    /// <summary>Trusted delimiter only when this composer generated the subaddress.</summary>
    public string? ClassificationSubAddressDelimiter => !FromOverrideEnabled && TaggedAddress() is not null ? _c.Settings.SubAddressDelimiter : null;

    public ClassificationPolicy? ClassificationPolicy => _classificationPolicy;
    public bool ClassificationReady => _classificationReady;

    public void BeginClassificationLoad(object? owner = null, string? identity = null)
    {
        _classificationPolicy = null;
        _classificationReady = false;
        if (!ReferenceEquals(_classificationOwner, owner) || !string.Equals(_classificationIdentity, identity, StringComparison.Ordinal))
            ClassificationChoice = null;
        _classificationOwner = owner;
        _classificationIdentity = identity;
        OnPropertyChanged(nameof(ClassificationPolicy));
        OnPropertyChanged(nameof(ClassificationReady));
    }

    public void SetClassificationPolicy(ClassificationPolicy? policy)
    {
        _classificationPolicy = policy;
        _classificationReady = policy is not null;
        if (policy is { List.Count: > 0 } && ClassificationChoice is null &&
            policy.List.Any(label => string.Equals(label.Id, policy.DefaultId, StringComparison.Ordinal)))
            ClassificationChoice = new ComposerClassificationChoice(policy.DefaultId);
        else if (policy is { List.Count: > 0, Required: false } && ClassificationChoice is null)
            ClassificationChoice = new ComposerClassificationChoice(null);
        OnPropertyChanged(nameof(ClassificationPolicy));
        OnPropertyChanged(nameof(ClassificationReady));
    }
}
