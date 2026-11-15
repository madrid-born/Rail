namespace Rail.Maui;

public partial class AppShell : Shell
{
    protected override bool OnBackButtonPressed()
    {
        if (CurrentPage is MainPage page) { _ = page.HandleBackAsync(); return true; }
        return base.OnBackButtonPressed();
    }
    public AppShell()
    {
        InitializeComponent();
    }
}
