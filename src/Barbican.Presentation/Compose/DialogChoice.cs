// Owner composer-ui. See ComposerDialog.

namespace Barbican.Presentation.Compose;

/// <summary>One button of a <see cref="ComposerDialog"/>.</summary>
public sealed record DialogChoice(string Id, string Label, DialogTone Tone = DialogTone.Plain);
