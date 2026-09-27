namespace FitGirlLauncher.Models;

/// <summary>One entry in the top navigation bar. Add items to MainViewModel.NavItems to grow the nav.</summary>
public class NavItem : ObservableObject
{
    public NavItem(string id, string label, string glyph)
    {
        Id = id;
        Label = label;
        Glyph = glyph;
    }

    public string Id { get; }
    public string Label { get; }
    public string Glyph { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
