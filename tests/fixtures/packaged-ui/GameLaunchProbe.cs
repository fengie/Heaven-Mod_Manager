using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

internal static class GameLaunchProbe
{
    [STAThread]
    private static void Main()
    {
        var receipt = Environment.GetEnvironmentVariable("MHW_DISPOSABLE_GAME_RECEIPT");
        if (string.IsNullOrEmpty(receipt)) return;
        using (var process = Process.GetCurrentProcess())
            File.WriteAllLines(receipt, new[] { process.MainModule.FileName, Environment.CurrentDirectory, process.Id.ToString() });
        // Stay alive through Manager's normal 15-second launch observation.
        for (var i = 0; i < 300 && !File.Exists(receipt + ".done"); i++)
            Thread.Sleep(100);
    }
}
