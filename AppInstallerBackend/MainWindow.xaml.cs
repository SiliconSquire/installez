using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AppInstallerBackend
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            InitializeWebView();
        }

        private async void InitializeWebView()
        {
            await webView.EnsureCoreWebView2Async();

            string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "InstallEz");
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "appassets", folderPath, CoreWebView2HostResourceAccessKind.Allow);

            webView.CoreWebView2.Navigate("https://appassets/index.html");

            webView.CoreWebView2.WebMessageReceived += WebMessageReceived;
        }



        private void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            var message = args.WebMessageAsJson;
            InstallApplications(message);
        }



        private async void InstallApplications(string jsonApps)
        {
            var appIds = System.Text.Json.JsonSerializer.Deserialize<string[]>(jsonApps);
            if (appIds != null)
            {
                var results = new List<string>();
                foreach (var appId in appIds)
                {
                    if (!IsAppAvailable(appId))
                    {
                        results.Add($"The app {appId} was not found in Winget.");
                        continue;
                    }

                    if (IsAppInstalled(appId))
                    {
                        results.Add($"The app {appId} is already installed.");
                        continue;
                    }

                    var success = await InstallApp(appId);
                    if (success)
                    {
                        results.Add($"Successfully installed {appId}.");
                    }
                    else
                    {
                        results.Add($"Failed to install {appId}.");
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    webView.CoreWebView2.PostWebMessageAsString(string.Join("\n", results));
                });
            }
        }

        private bool IsAppAvailable(string appId)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = $"search {appId}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                var process = new Process
                {
                    StartInfo = processInfo
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return !output.Contains("No package found");
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"An error occurred while checking if {appId} is available: {ex.Message}");
                });
                return false;
            }
        }


        private bool IsAppInstalled(string appId)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = $"list {appId}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                var process = new Process
                {
                    StartInfo = processInfo
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output.Contains(appId);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"An error occurred while checking if {appId} is installed: {ex.Message}");
                });
                return false;
            }
        }
        private async Task<bool> InstallApp(string appId)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = $"install {appId} --silent --accept-source-agreements --accept-package-agreements",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = new Process
                {
                    StartInfo = processInfo,
                    EnableRaisingEvents = true
                };

                var tcs = new TaskCompletionSource<bool>();
                var output = new List<string>();

                process.OutputDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        output.Add(args.Data);
                        Dispatcher.Invoke(() =>
                        {
                            webView.CoreWebView2.PostWebMessageAsString(args.Data);
                        });
                    }
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        output.Add(args.Data);
                        Dispatcher.Invoke(() =>
                        {
                            webView.CoreWebView2.PostWebMessageAsString($"Error: {args.Data}");
                        });
                    }
                };

                process.Exited += (sender, args) =>
                {
                    tcs.TrySetResult(process.ExitCode == 0);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await tcs.Task;

                // Check if TOS agreement was needed and re-run the command with TOS acceptance if required
                if (output.Any(line => line.Contains("Terms of Service")))
                {
                    processInfo.Arguments = $"install {appId} --silent --accept-source-agreements --accept-package-agreements";
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await tcs.Task;
                }

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"An error occurred while installing {appId}: {ex.Message}");
                });
                return false;
            }
        }

    }
}