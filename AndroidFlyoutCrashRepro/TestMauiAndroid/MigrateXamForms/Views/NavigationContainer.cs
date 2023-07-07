namespace MigrateXamForms.Views
{
    public class NavigationContainer : FlyoutPage
    {
        public NavigationContainer()
        {
            var menuPage = new ContentPage
            {
                Title = "Title",
                Padding = new Thickness(0, 20, 0, 0)
            };

            var listView = new ListView
            {
                VerticalOptions = LayoutOptions.FillAndExpand,
                BackgroundColor = Colors.White,
                Margin = new Thickness(0, 20, 0, 0),
                ItemsSource = new List<Page>()
            };

            var label = new Label
            {
                Text = "Privacy Policy",
                TextColor = Colors.Blue,
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(10, 20)
            };

            var footerLayout = new StackLayout();
            footerLayout.Children.Add(label);

            var tgr = new TapGestureRecognizer();
            tgr.SetBinding(TapGestureRecognizer.CommandProperty, "OnLabelTappedCommand");
            label.GestureRecognizers.Add(tgr);

            footerLayout.Children.Add(label);

            //listView.Footer = footerLayout;

            menuPage.Content = listView;

            var otherLabel = new Label
            {
                Text = $"Connect{Environment.NewLine}by Intermatic",
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                FontSize = 20,
                TextColor = Colors.White,
                Margin = 0
            };

            var grid = new Grid
            {
                RowDefinitions = new RowDefinitionCollection
                {
                    new RowDefinition { Height = GridLength.Auto }
                },
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                },
            };

            grid.Children.Add(otherLabel);

            var versionLabel = new Label
            {
                Text = "Some text",
                HorizontalTextAlignment = TextAlignment.Center,
                FontSize = 12,
                TextColor = Colors.White,
                Margin = new Thickness(10, 40, 10, 10),
                Padding = new Thickness(0, 10)
            };

            var mainStackLayout = new StackLayout
            {
                Spacing = 0,
                VerticalOptions = LayoutOptions.FillAndExpand
            };
            mainStackLayout.Children.Add(grid);
            mainStackLayout.Children.Add(listView);
            mainStackLayout.Children.Add(versionLabel);

            var cp = new ContentPage
            {
                Content = mainStackLayout,
                Title = "Menu"
            };

            Detail = new NavigationPage();

            Flyout = cp;
        }
    }
}