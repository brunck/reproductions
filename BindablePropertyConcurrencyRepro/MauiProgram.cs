using Microsoft.Extensions.Logging;

namespace BindablePropertyConcurrencyRepro;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// Installed first so a crash during startup is still captured.
		ExceptionReporter.Install();

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
