namespace SyncfusionPickerMemoryLeak;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Simple NavigationPage root so MainPage can call PushModalAsync.
        return new Window(new NavigationPage(new MainPage()));
    }
}
