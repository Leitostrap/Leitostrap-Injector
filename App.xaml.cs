using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;


namespace LeitostrapV7;


public partial class App : Application
{
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "leitostrap_crash.log");


    static App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                var ex = args.ExceptionObject as Exception;
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now}] APPDOMAIN: {ex}\n{ex?.StackTrace}\n\n");
            }
            catch { }
        };
    }


    private void OnStartup(object sender, StartupEventArgs e)
    {
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            try
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now}] UNOBSERVED TASK: {args.Exception}\n{args.Exception?.StackTrace}\n\n");
            }
            catch { }
            args.SetObserved();
        };


        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now}] DISPATCHER: {args.Exception}\n{args.Exception?.StackTrace}\n\n");
            }
            catch { }
            args.Handled = true;
        };


        try
        {
            string? installLang = Core.SettingsService.Instance.Get<string>("InstallLanguage", "en");
            Core.LanguageService.Instance.Load(installLang);
        }
        catch { Core.LanguageService.Instance.Load("en"); }


        if (e.Args.Length > 0)
        {
            if (e.Args[0] == "--uninstall")
            {
                string installPath = Core.SettingsService.Instance.Get<string>("InstallPath") ?? "";
                if (string.IsNullOrEmpty(installPath))
                    installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Leitostrap");


                if (System.Diagnostics.Process.GetProcessesByName("LeitostrapV7").Length > 1)
                {
                    MessageBox.Show("Please close all Leitostrap instances before uninstalling.", "Uninstall",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    Shutdown();
                    return;
                }


                Installer.InstallerService.Uninstall(installPath);
                Shutdown();
                return;
            }


            if (e.Args[0] == "--installer")
            {
                ShowInstallerFlow();
                return;
            }
        }


        bool isInstalled;
        try { isInstalled = Core.SettingsService.Instance.Get<bool>("Installed", false); }
        catch { isInstalled = false; }


        if (!isInstalled)
        {
            ShowInstallerFlow();
        }
        else
        {
            ShowMain();
        }
    }


    private void ShowInstallerFlow()
    {
        if (!Installer.InstallerService.IsAdmin())
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "",
                    Arguments = "--installer",
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch { }
            Shutdown();
            return;
        }


        ShowInstallerThenMain();
    }


    private void ShowInstallerThenMain()
    {
        var installer = new Installer.InstallerWindow();
        installer.InstallCompleted += (_, _) => ShowMain();
        MainWindow = installer;
        installer.Show();
    }


    private void ShowMain()
    {
        if (MainWindow is Views.MainWindow)
            return;


        var oldInstaller = MainWindow as Installer.InstallerWindow;


        var main = new Views.MainWindow();
        MainWindow = main;
        main.Show();


        oldInstaller?.Close();
    }
}
