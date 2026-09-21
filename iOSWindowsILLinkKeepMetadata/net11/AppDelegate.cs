namespace TrimTest11;

[Register ("AppDelegate")]
public class AppDelegate : UIApplicationDelegate {
	public override bool FinishedLaunching (UIApplication application, NSDictionary? launchOptions)
	{
		// Reflection-serialize a type from a trimmed NuGet assembly. When the build is driven from Windows
		// (Pair to Mac), ILLink strips constructor parameter names and System.Text.Json throws NotSupportedException.
		try {
			TrimProbe.Touch ();
			Console.WriteLine ("TrimProbe: OK, constructor parameter names survived trimming");
		} catch (Exception e) {
			Console.WriteLine ($"TrimProbe: FAILED: {e}");
		}
		return true;
	}

	public override UISceneConfiguration GetConfiguration (UIApplication application, UISceneSession connectingSceneSession, UISceneConnectionOptions options)
	{
		return new UISceneConfiguration ("Default Configuration", connectingSceneSession.Role);
	}

	public override void DidDiscardSceneSessions (UIApplication application, NSSet<UISceneSession> sceneSessions)
	{
	}
}
