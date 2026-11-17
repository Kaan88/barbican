// Mirrors: M2 plan section 4.2 (DialogRequest; content records per dialog live with their owner); PORT-PLAN section 3.7 (a custom host, never ContentDialog). Contract from stage F of M2; owner menus.

namespace Barbican.Presentation.Dialogs;

/// <summary>A dialog to show; the result type is what the dialog answers with (a folder id, true for OK).</summary>
public abstract record DialogRequest<TResult>(string Title);
