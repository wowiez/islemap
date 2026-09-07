using System.Windows;
using Velopack;

namespace TheIsleOverlay.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        if (!SingleInstanceLease.TryAcquire(
                @"Local\Wowiez.IsleLiveMap.SingleInstance",
                out var instanceLease))
        {
            return;
        }

        using (instanceLease)
        {
            var application = new App();
            application.InitializeComponent();
            application.Run();
        }
    }
}
