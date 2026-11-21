// Mirrors: Bulwark components/contacts/contact-list-item.tsx:75-133 (a full-width row: px-4, density-item-py 12, gap 12, border-b border-border, hover bg-muted, selected bg-selection, a 16 px checkbox once anything is ticked, a 32 px avatar, name text-sm font-medium, email text-xs muted; the organization line is the comfortable density, not the default regular),
// contact-list.tsx:337-339 (letter header px-4 py-0.5 text-[0.6875rem] font-medium muted on the background); B-CON-027, B-CON-030; spec/layout/contacts.md (Bento and Nordic keep the rounded 52 px row, avatar 36, section label 30). One item of the contact list: a letter header or a row.

using Barbican.App.Theme;
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
    private readonly bool _bulwark;

    internal ContactsListItem(string letter)
    {
        Letter = letter;
        _bulwark = StyleState.Current.IsBulwark;
    }

    internal ContactsListItem(ContactsRow row, bool selected, bool selectMode, bool bento)
    {
        Row = row;
        Letter = row.Letter;
        _selected = selected;
        _selectMode = selectMode;
        _bento = bento;
        _bulwark = StyleState.Current.IsBulwark;
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

    public Brush AvatarBackground => Photo is not null ? Photo : Tint.Background;

    public Brush AvatarForeground => Tint.Foreground;

    // avatar.tsx:339-345: size sm is 32 and a circle; the others keep their 36 squircle or circle
    public double AvatarSize => _bulwark ? 32 : 36;

    public double AvatarFontSize => _bulwark ? 12 : 12.5;

    public CornerRadius AvatarRadius => _bulwark ? new CornerRadius(16) : ContactsAvatar.Radius(_bento, 36);

    public FontWeight AvatarWeight => _bulwark ? FontWeights.SemiBold : FontWeights.Bold;

    // a row: Bulwark is full width with a hairline under it (border-b border-border); the others are rounded cards
    public double RowHeight => _bulwark ? double.NaN : 52;

    public Thickness RowPadding => _bulwark ? new Thickness(16, 12, 16, 12) : new Thickness(10, 0, 10, 0);

    public double RowSpacing => _bulwark ? 12 : 11;

    public CornerRadius RowCorner => _bulwark ? new CornerRadius(0) : new CornerRadius(16);

    public Thickness RowBorder => _bulwark ? new Thickness(0, 0, 0, 1) : new Thickness(0);

    public Brush RowBackground => _selected ? ContactsUi.Palette(_bulwark ? "FeedbackSelected" : "Sel") : Clear;

    public Brush NameBrush => ContactsUi.Palette(_selected && !_bulwark ? "SelText" : "Text");

    public double NameSize => _bulwark ? 14 : 13.5;

    public FontWeight NameWeight => _bulwark ? FontWeights.Medium : _selected ? FontWeights.SemiBold : FontWeights.Normal;

    public Brush SubBrush => ContactsUi.Palette(_bulwark ? "Dim" : "Faint");

    // the letter header
    public double HeaderHeight => _bulwark ? 22 : 30;

    public Thickness HeaderPadding => _bulwark ? new Thickness(16, 0, 16, 0) : new Thickness(12, 8, 0, 0);

    public double HeaderSize => _bulwark ? 11 : 11.5;

    public FontWeight HeaderWeight => _bulwark ? FontWeights.Medium : FontWeights.Bold;

    public Brush HeaderBrush => ContactsUi.Palette(_bulwark ? "Dim" : "Faint");

    public Brush HeaderBackground => _bulwark ? ContactsUi.Palette("Bg") : Clear;

    public Visibility CheckVisible => _selectMode && !IsHeader ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CheckMarkVisible => _selected ? Visibility.Visible : Visibility.Collapsed;

    // contact-list-item.tsx:97-104: a ticked box is primary, an empty one muted
    public Brush CheckBrush => ContactsUi.Palette(_selected ? "Accent" : "Dim");

    private (Brush Background, Brush Foreground) Tint => _bulwark
        ? ContactsAvatar.BulwarkTint(Row?.DisplayName ?? string.Empty, Row?.Email)
        : ContactsAvatar.Tint(Row?.DisplayName ?? string.Empty);
}
