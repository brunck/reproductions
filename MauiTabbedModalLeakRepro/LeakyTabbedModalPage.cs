using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;

namespace MauiTabbedModalLeakRepro;


public sealed class LeakyTabbedModalPage : Microsoft.Maui.Controls.TabbedPage
{
    private readonly Page _settingsTab;

    public LeakyTabbedModalPage(bool wrapTabsInNavigationPage)
    {
        Title = "Device Detail (Repro)";

        On<Microsoft.Maui.Controls.PlatformConfiguration.Android>().SetToolbarPlacement(ToolbarPlacement.Bottom);

        var overview = CreateTabPage("Overview");
        var schedule = CreateTabPage("Schedule");
        var settings = CreateTabPage("Settings");

        if (wrapTabsInNavigationPage)
        {
            var overviewNav = new NavigationPage(overview) { Title = overview.Title };
            var scheduleNav = new NavigationPage(schedule) { Title = schedule.Title };
            var settingsNav = new NavigationPage(settings) { Title = settings.Title };

            Children.Add(overviewNav);
            Children.Add(scheduleNav);
            Children.Add(settingsNav);
            _settingsTab = settingsNav;
        }
        else
        {
            Children.Add(overview);
            Children.Add(schedule);
            Children.Add(settings);
            _settingsTab = settings;
        }
    }

    public void SwitchToSettingsTab()
    {
        CurrentPage = _settingsTab;
    }

    private static ContentPage CreateTabPage(string title)
    {
        return new ContentPage
        {
            Title = title,
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new Label { Text = $"{title} tab" },
                    new Label { Text = "This is a minimal page for leak repro." }
                }
            }
        };
    }
}
