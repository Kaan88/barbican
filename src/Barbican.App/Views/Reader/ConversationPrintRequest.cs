using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Mail;

namespace Barbican.App.Views.Reader;

internal sealed record ConversationPrintRequest(ConversationExpansion Card) : DialogRequest<bool>("Print message");
