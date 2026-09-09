using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;


namespace LeitostrapV7.Installer;


public static class InstallerService
{


    public const string NetDownloadUrl =
        "https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime?utm_source=leitostrap";


    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }


    public static void ElevateAndRelaunch()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "",
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch { }
    }


    public static void Install(string installPath, bool createDesktopShortcut, bool runAsAdmin, bool excludeAntivirus, string version)
    {
        Directory.CreateDirectory(installPath);


        string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
        {
            string destExe = Path.Combine(installPath, "LeitostrapV7.exe");
            File.Copy(currentExe, destExe, true);
        }
        else
        {
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "LeitostrapV7.exe"))
                File.Copy(file, Path.Combine(installPath, Path.GetFileName(file)), true);
        }


        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith("LeitostrapV7.", StringComparison.OrdinalIgnoreCase) && name != "LeitostrapV7.exe")
                File.Copy(file, Path.Combine(installPath, name), true);
        }


        string localResources = Path.Combine(AppContext.BaseDirectory, "Resources");
        if (Directory.Exists(localResources))
            CopyDirectoryRecursive(localResources, Path.Combine(installPath, "Resources"));


        if (createDesktopShortcut)
            CreateDesktopShortcut(installPath);


        if (runAsAdmin)
            SetRunAsAdmin(Path.Combine(installPath, "LeitostrapV7.exe"));


        if (excludeAntivirus)
            AddAntivirusExclusion(installPath);


        RegisterUninstall(installPath, version);
    }


    public static void OpenNetDownload()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = NetDownloadUrl, UseShellExecute = true });
        }
        catch { }
    }


    public static void Uninstall(string installPath)
    {
        try
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string shortcutPath = Path.Combine(desktopPath, "Leitostrap Injector.lnk");
            if (File.Exists(shortcutPath)) File.Delete(shortcutPath);


            RemoveAntivirusExclusion(installPath);
            UnregisterUninstall();


            if (Directory.Exists(installPath))
                Directory.Delete(installPath, true);


            try
            {
                Core.SettingsService.Instance.Set("Installed", false);
                Core.SettingsService.Instance.Set("InstallPath", "");
                Core.SettingsService.Instance.Save();
            }
            catch { }


            MessageBox.Show("Leitostrap has been uninstalled successfully.", "Uninstall Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Uninstall error: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private static void CopyDirectoryRecursive(string source, string dest)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }


    private static void CreateDesktopShortcut(string installPath)
    {
        try
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string shortcutPath = Path.Combine(desktopPath, "Leitostrap Injector.lnk");
            string exePath = Path.Combine(installPath, "LeitostrapV7.exe");


            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = installPath;
            shortcut.Description = "Leitostrap Injector - Roblox FFlag Injector";
            string iconPath = Path.Combine(installPath, "LeitostrapV7.exe");
            shortcut.IconLocation = iconPath + ",0";
            shortcut.Save();
            Marshal.ReleaseComObject(shell);
        }
        catch { }
    }


    private static void SetRunAsAdmin(string exePath)
    {
        try
        {
            string manifest = $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<assembly xmlns=""urn:schemas-microsoft-com:asm.v1"" manifestVersion=""1.0"">
  <trustInfo xmlns=""urn:schemas-microsoft-com:asm.v3"">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level=""requireAdministrator"" uiAccess=""false""/>
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>";
            string manifestPath = exePath + ".manifest";
            File.WriteAllText(manifestPath, manifest);
        }
        catch { }
    }


    private static void AddAntivirusExclusion(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Add-MpPreference -ExclusionPath '{path}'\"",
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi)?.WaitForExit(5000);
        }
        catch { }
    }


    private static void RemoveAntivirusExclusion(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Remove-MpPreference -ExclusionPath '{path}'\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi)?.WaitForExit(5000);
        }
        catch { }
    }


    public static void RegisterUninstall(string installPath, string version)
    {
        try
        {
            string regKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LeitostrapV7";
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(regKey);
            if (key != null)
            {
                key.SetValue("DisplayName", "Leitostrap Injector V7.0.0");
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", "Leitostrap");
                key.SetValue("InstallLocation", installPath);
                string exeFullPath = Path.Combine(installPath, "LeitostrapV7.exe");
                key.SetValue("UninstallString", $"\"{exeFullPath}\" --uninstall");
                key.SetValue("QuietUninstallString", $"\"{exeFullPath}\" --uninstall --quiet");
                key.SetValue("DisplayIcon", Path.Combine(installPath, "LeitostrapV7.exe"));
                key.SetValue("EstimatedSize", 50000);
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
                key.SetValue("URLInfoAbout", "https://leitostrap.netlify.app/");
            }
        }
        catch { }
    }


    public static void UnregisterUninstall()
    {
        try
        {
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LeitostrapV7", false);
        }
        catch { }
    }
}
