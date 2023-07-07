using Autofac.Extensions.DependencyInjection;

namespace MigrateXamForms;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp(_ =>
            {
                return new App();
            })
            .ConfigureContainer(new AutofacServiceProviderFactory());

        return builder.Build();
    }
}
