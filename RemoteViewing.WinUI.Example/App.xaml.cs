#region License

/*
RemoteViewing VNC Client/Server Library for .NET
Copyright (c) 2013, 2025 James F. Bellinger <http://software.seekye.com/remoteviewing>
All rights reserved.
*/

#endregion

using Microsoft.UI.Xaml;

namespace RemoteViewing.WinUI.Example;

public partial class App : Application
{
    private Window _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
