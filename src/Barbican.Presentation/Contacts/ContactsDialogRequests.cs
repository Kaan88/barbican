// Mirrors: M2 plan section 4.2 (content records per dialog live with their owner); PORT-PLAN section 3.7. The dialogs of the Contacts app, each a request the dialog host draws through an IDialogContent of App/Views/Contacts. Owner con-ui.

#pragma warning disable MA0048 // seven small records of one family live in one file
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Contacts;

/// <summary>The contact form (new or edit); the answer is true when the contact was saved.</summary>
public sealed record ContactsFormRequest(ContactsFormModel Model) : DialogRequest<bool>(Model?.Heading ?? "Contact");

/// <summary>The group form with the member picker.</summary>
public sealed record ContactsGroupRequest(ContactsGroupModel Model) : DialogRequest<bool>(Model?.Heading ?? "Group");

/// <summary>The import dialog.</summary>
public sealed record ContactsImportRequest(ContactsImportViewModel Model) : DialogRequest<bool>("Import contacts");

/// <summary>The merge dialog over the sets of duplicates.</summary>
public sealed record ContactsMergeRequest(ContactsMergeViewModel Model) : DialogRequest<bool>("Duplicate contacts");

/// <summary>The in-window print preview of contacts (D-M5-12).</summary>
public sealed record ContactsPrintRequest(ContactsPrintDocument Document) : DialogRequest<bool>("Print");

/// <summary>The directory browser (the composer's directory button and the Directory place): the answer is the chosen addresses joined with commas, or null.</summary>
public sealed record ContactsDirectoryRequest(ContactsDirectoryModel Model) : DialogRequest<string>("Directory");
