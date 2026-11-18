// Mirrors: Bulwark components/contacts/contact-list-item.tsx (avatar, name, email line, a check in select mode), contact-list.tsx (letter headers); B-CON-027, B-CON-030; spec/layout/contacts.md (row 52 high, avatar 36, section label 30). One item of the contact list: a letter header or a row.

using Barbican.Presentation.Contacts;
using Microsoft.UI.Text;
using Windows.UI.Text;

namespace Barbican.App.Views.Contacts;

/// <summary>What the list template binds to: a letter header (no <see cref="Row"/>) or a contact row.</summary>
public sealed class ContactsListItem
{
    private static readonly SolidColorBrush Clear = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    private readonly bool _selected;
    private readonly bool _bento;
    private readonly bool _selectMode;

    internal ContactsListItem(string letter)
    {
        Letter = letter;
    }

    internal ContactsListItem(ContactsRow row, bool selected, bool selectMode, bool bento)
    {
        Row = row;
        Letter = row.Letter;
        _selected = selected;
        _selectMode = selectMode;
        _bento = bento;
        Photo = ContactsAvatar.Photo(row.PhotoUri);
    }

    public ContactsRow? Row { get; }

    public string Letter { get; }

    public Microsoft.UI.Xaml.Media.ImageBrush? Photo { get; }

    public bool IsHeader => Row is null;

    public Visibility HeaderVisible => IsHeader ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RowVisible => IsHeader ? Visibility.Collapsed : Visibility.Visible;

    public string Name => Row?.DisplayName ?? string.Empty;

    public string Email => Row?.Email ?? string.Empty;

    public Visibility EmailVisible => string.IsNullOrEmpty(Row?.Email) || string.Equals(Row?.Email, Row?.DisplayName, StringComparison.Ordinal) ? Visibility.Collapsed : Visibility.Visible;

    public string Initials => Row?.Initials ?? string.Empty;

    public Visibility InitialsVisible => Photo is null ? Visibility.Visible : Visibility.Collapsed;

    public Brush AvatarBackground => Photo is not null ? Photo : ContactsAvatar.Tint(Row?.DisplayName ?? string.Empty).Background;

    public Brush AvatarForeground => ContactsAvatar.Tint(Row?.DisplayName ?? string.Empty).Foreground;

    public CornerRadius AvatarRadius => ContactsAvatar.Radius(_bento, 36);

    public Brush RowBackground => _selected ? ContactsUi.Palette("Sel") : Clear;

    public Brush NameBrush => ContactsUi.Palette(_selected ? "SelText" : "Text");

    public Visibility CheckVisible => _selectMode && !IsHeader ? Visibility.Visible : Visibility.Collapsed;

    public string Check => _selected ? "☑" : "☐";

    public FontWeight NameWeight => _selected ? FontWeights.SemiBold : FontWeights.Normal;
}
