using System.Reflection;
using System.IO;
using LanDeviceMonitor.Services;

internal static class SettingsConnectionRegression
{
    public static async Task<int> RunAsync()
    {
        using var cancelled = new CancellationTokenSource();cancelled.Cancel();
        using var router = new XiaomiRouterClient();router.Configure("http://127.0.0.1:1","synthetic-test");
        try { await router.AuthenticateAsync(cancelled.Token);throw new Exception("取消应传递给调用方"); }
        catch(OperationCanceledException) { Console.WriteLine("PASS intentional authentication cancellation is not reported as router failure"); }
        typeof(XiaomiRouterClient).GetField("_token",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(router,"synthetic-token");
        var method=typeof(XiaomiRouterClient).GetMethod("GetApiDocumentAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
        try { await (Task)method.Invoke(router,["api/test",cancelled.Token])!;throw new Exception("取消应传递给轮询"); }
        catch(OperationCanceledException) { Console.WriteLine("PASS intentional polling cancellation is not reported as connection error"); }
        return 0;
    }
}
