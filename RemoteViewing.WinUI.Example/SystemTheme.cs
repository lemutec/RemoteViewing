#region License

/*
RemoteViewing VNC Client/Server Library for .NET
Copyright (c) 2013, 2025 James F. Bellinger <http://software.seekye.com/remoteviewing>
All rights reserved.
*/

#endregion

using Windows.UI.ViewManagement;

namespace RemoteViewing.WinUI.Example;

internal static class SystemTheme
{
    public static bool IsDark()
    {
        var background = new UISettings().GetColorValue(UIColorType.Background);
        return background.R + background.G + background.B < 128 * 3;
    }
}
