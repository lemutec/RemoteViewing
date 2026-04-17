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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RemoteViewing.Vnc;

namespace RemoteViewing.Avalonia.Example;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _statisticsTimer;

    public MainWindow()
    {
        InitializeComponent();
        UpdateTitle();

        _statisticsTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _statisticsTimer.Tick += StatisticsTimer_Tick;
        _statisticsTimer.Start();
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (VncControl.Client.IsConnected)
        {
            VncControl.Client.Close();
            return;
        }

        var hostname = (TxtHostname.Text ?? string.Empty).Trim();
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
        var password = TxtPassword.Text ?? string.Empty;
        if (password.Length != 0)
        {
            options.Password = password.ToCharArray();
        }

        SetConnectingState(true);

        try
        {
            try
            {
                await Task.Run(() => VncControl.Client.Connect(hostname, port, options));
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

            VncControl.Focus();
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
        Cursor = connecting ? new Cursor(StandardCursorType.Wait) : new Cursor(StandardCursorType.Arrow);

        if (connecting)
        {
            BtnConnect.Content = "Connecting...";
        }
    }

    private void VncControl_Connected(object sender, EventArgs e)
    {
        BtnConnect.Content = "Close";
    }

    private void VncControl_Closed(object sender, EventArgs e)
    {
        BtnConnect.Content = "Connect";
    }

    private void VncControl_ConnectionFailed(object sender, EventArgs e)
    {
    }

    private void ChkShowFps_IsCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (VncControl != null)
        {
            VncControl.ShowFps = ChkShowFps.IsChecked == true;
        }
    }

    private void StatisticsTimer_Tick(object sender, EventArgs e)
    {
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string title = "RemoteViewing - Example VNC Client (Avalonia)";

        if (VncControl?.Client != null)
        {
            VncClientStatistics stats = VncControl.Client.GetStatistics();
            double recv = stats.BytesReceivedPerSecond;
            double send = stats.BytesSentPerSecond;
            int cpu = (int)Math.Round(stats.CpuUsage * 100);
            double fps = VncControl.CurrentFps;

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

    /// <summary>
    /// Minimal message box substitute: Avalonia does not ship a built-in MessageBox,
    /// so we use a dialog window with a text block and an OK button.
    /// </summary>
    private Task ShowMessage(string title, string message)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var panel = new DockPanel { Margin = new global::Avalonia.Thickness(12) };
        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 26,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
        };
        okButton.Click += (_, _) => dlg.Close();
        DockPanel.SetDock(okButton, Dock.Bottom);
        panel.Children.Add(okButton);

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };
        panel.Children.Add(text);

        dlg.Content = panel;
        return dlg.ShowDialog(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _statisticsTimer?.Stop();
        try { VncControl?.Client?.Close(); }
        catch { }
    }
}
