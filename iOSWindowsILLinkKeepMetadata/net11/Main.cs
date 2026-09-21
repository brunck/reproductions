using TrimTest11;

// This is the main entry point of the application.
// If you want to use a different Application Delegate class from "AppDelegate"
// you can specify it here.
UIApplication.Main (args, null, typeof (AppDelegate));

static class TrimProbe
{
    // Mirrors the app: MQTTnet constructs the result, then the reflection-based STJ serializer dumps it.
    public static void Touch()
    {
        var result = new MQTTnet.MqttClientPublishResult(null, default, null, null);
        System.Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        var options = new MQTTnet.MqttClientOptionsBuilder().WithCleanSession(true).Build();
        System.Console.WriteLine(options.CleanSession);
    }
}
