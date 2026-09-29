#region License

/*
RemoteViewing VNC Client/Server Library for .NET
Copyright (c) 2013, 2025 James F. Bellinger <http://software.seekye.com/remoteviewing>
All rights reserved.
*/

#endregion

using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RemoteViewing.Vnc;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace RemoteViewing.WinUI.Example;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _statisticsTimer;
    private readonly UISettings _uiSettings = new();

    public MainWindow()
    {
        InitializeComponent();
        ApplySystemTheme();
        _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        _statisticsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _statisticsTimer.Tick += StatisticsTimer_Tick;
        _statisticsTimer.Start();
        Closed += MainWindow_Closed;
        UpdateTitle();
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (Vnc.Client != null && Vnc.Client.IsConnected)
        {
            Vnc.Client.Close();
            return;
        }

        string hostname = (TxtHostname.Text ?? string.Empty).Trim();
        if (hostname.Length == 0)
        {
            await ShowMessage("Hostname", "Hostname isn't set.");
            return;
        }

        if (!int.TryParse(TxtPort.Text, out int port) || port < 1 || port > 65535)
        {
            await ShowMessage("Port", "Port must be between 1 and 65535.");
            return;
        }

        var options = new VncClientConnectOptions();
        string password = TxtPassword.Password ?? string.Empty;
        if (password.Length != 0)
        {
            options.Password = password.ToCharArray();
        }

        SetConnectingState(true);

        try
        {
            try
            {
                await Task.Run(() => Vnc.Client.Connect(hostname, port, options));
            }
            catch (VncException ex)
            {
                await ShowMessage("Connect", "Connection failed (" + ex.Reason + ").");
                return;
            }
            catch (SocketException ex)
            {
                await ShowMessage("Connect", "Connection failed (" + ex.SocketErrorCode + ").");
                return;
            }

            Vnc.Focus(FocusState.Programmatic);
        }
        finally
        {
            if (options.Password != null)
            {
                Array.Clear(options.Password, 0, options.Password.Length);
            }

            SetConnectingState(false);
        }
    }

    private void SetConnectingState(bool connecting)
    {
        BtnConnect.IsEnabled = !connecting;
        TxtHostname.IsEnabled = !connecting;
        TxtPort.IsEnabled = !connecting;
        TxtPassword.IsEnabled = !connecting;

        if (connecting)
        {
            BtnConnect.Content = "Connecting...";
        }
    }

    private void Vnc_Connected(object sender, EventArgs e)
    {
        BtnConnect.Content = "Close";
    }

    private void Vnc_Closed(object sender, EventArgs e)
    {
        BtnConnect.Content = "Connect";
    }

    private void Vnc_ConnectionFailed(object sender, EventArgs e)
    {
    }

    private void ChkShowFps_Changed(object sender, RoutedEventArgs e)
    {
        if (Vnc != null)
        {
            Vnc.ShowFps = ChkShowFps.IsChecked == true;
        }
    }

    private void StatisticsTimer_Tick(object sender, object e)
    {
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string title = "RemoteViewing - Example VNC Client (WinUI)";

        if (Vnc?.Client != null)
        {
            VncClientStatistics stats = Vnc.Client.GetStatistics();
            double recv = stats.BytesReceivedPerSecond;
            double send = stats.BytesSentPerSecond;
            int cpu = (int)Math.Round(stats.CpuUsage * 100);
            double fps = Vnc.CurrentFps;

            if (recv / 1024 >= 0.1 || send / 1024 >= 0.1 || cpu > 0 || fps > 0)
            {
                title += string.Format(
                    " - {0} KB/s received, {1} KB/s sent, {2}% CPU, {3} FPS",
                    (recv / 1024).ToString("0.0"),
                    (send / 1024).ToString("0.0"),
                    cpu,
                    fps.ToString("0"));
            }
        }

        Title = title;
    }

    private async Task ShowMessage(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
        };

        await dialog.ShowAsync();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs e)
    {
        _uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;
        _statisticsTimer.Stop();
        try { Vnc?.Client?.Close(); }
        catch { }
    }

    private void UiSettings_ColorValuesChanged(UISettings sender, object args)
    {
        DispatcherQueue.TryEnqueue(ApplySystemTheme);
    }

    private void ApplySystemTheme()
    {
        Root.RequestedTheme = SystemTheme.IsDark() ? ElementTheme.Dark : ElementTheme.Light;
        ApplyTitleBar();
    }

    private void ApplyTitleBar()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        bool dark = Root.ActualTheme == ElementTheme.Dark;
        Color background = dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
        Color foreground = dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
        Color inactiveForeground = dark ? Color.FromArgb(255, 160, 160, 160) : Color.FromArgb(255, 102, 102, 102);
        Color hover = dark ? Color.FromArgb(255, 51, 51, 51) : Color.FromArgb(255, 230, 230, 230);
        Color pressed = dark ? Color.FromArgb(255, 68, 68, 68) : Color.FromArgb(255, 214, 214, 214);

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = inactiveForeground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
    }
}
