using Avalonia.Interactivity;

namespace NetOpsTerminal.App.Views;

public sealed partial class ExplorerNameWindow : ThemedWindow
{
    private readonly bool _isSite;

    public ExplorerNameWindow() : this(false, "Unassigned")
    {
    }

    public ExplorerNameWindow(bool isSite, string location, string? existingName = null)
    {
        _isSite = isSite;
        InitializeComponent();
        bool isRename = existingName is not null;
        HeadingText.Text = isRename
            ? isSite ? "Rename site" : "Rename Organization folder"
            : isSite ? "Create site" : "Create folder";
        NameLabel.Text = isSite ? "Site name" : "Folder name";
        CreateButton.Content = isRename
            ? "Save name"
            : isSite ? "Create site" : "Create folder";
        LocationText.Text = isSite
            ? isRename ? "Rename this site in Device Explorer." : "Add a site to Device Explorer."
            : $"Location: {location}";
        NameTextBox.Text = existingName;
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Create_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string name = (NameTextBox.Text ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 120 || name.Any(char.IsControl))
                throw new ArgumentException("Enter a name of 120 characters or fewer.");
            if (!_isSite && name.Contains('/'))
                throw new ArgumentException("Folder names cannot contain a slash.");
            Close(name);
        }
        catch (ArgumentException exception)
        {
            ValidationText.Text = exception.Message;
            ValidationText.IsVisible = true;
        }
    }
}
