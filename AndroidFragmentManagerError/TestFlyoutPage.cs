namespace AndroidFragmentManagerError;

public class TestFlyoutPage : FlyoutPage
{
    public TestFlyoutPage()
    {
        Flyout = new FlyoutMenuPage();
        Detail = new NavigationPage(new MainPage());
    }
}