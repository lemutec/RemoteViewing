#region License

/*
RemoteViewing VNC Client/Server Library for .NET
Copyright (c) 2013 James F. Bellinger <http://software.seekye.com/remoteviewing>
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

#endregion

using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RemoteViewing.Windows.Forms.Example;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
        UpdateTitle();
    }

    private async void btnConnect_Click(object sender, EventArgs e)
    {
        if (vncControl.Client.IsConnected)
        {
            vncControl.Client.Close();
        }
        else
        {
            var hostname = txtHostname.Text.Trim();
            if (hostname == string.Empty)
            {
                MessageBox.Show(this, "Hostname isn't set.", "Hostname",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!int.TryParse(txtPort.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show(this, "Port must be between 1 and 65535.", "Port",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var options = new Vnc.VncClientConnectOptions();
            if (txtPassword.Text != string.Empty) { options.Password = txtPassword.Text.ToCharArray(); }

            // Disable UI during connection attempt
            SetConnectingState(true);

            try
            {
                try
                {
                    // Run the blocking Connect call on a background thread
                    await Task.Run(() => vncControl.Client.Connect(hostname, port, options));
                }
                catch (Vnc.VncException ex)
                {
                    MessageBox.Show(this,
                                    "Connection failed (" + ex.Reason.ToString() + ").",
                                    "Connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                catch (SocketException ex)
                {
                    MessageBox.Show(this,
                                    "Connection failed (" + ex.SocketErrorCode.ToString() + ").",
                                    "Connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                vncControl.Focus();
            }
            finally
            {
                if (options.Password != null)
                {
                    Array.Clear(options.Password, 0, options.Password.Length);
                }

                // Restore UI state
                SetConnectingState(false);
            }
        }
    }

    private void SetConnectingState(bool connecting)
    {
        btnConnect.Enabled = !connecting;
        txtHostname.Enabled = !connecting;
        txtPort.Enabled = !connecting;
        txtPassword.Enabled = !connecting;
        Cursor = connecting ? Cursors.WaitCursor : Cursors.Default;

        if (connecting)
        {
            btnConnect.Text = "Connecting...";
        }
    }

    private void vncControl_Connected(object sender, EventArgs e)
    {
        btnConnect.Text = "Close";
    }

    private void vncControl_Closed(object sender, EventArgs e)
    {
        btnConnect.Text = "Connect";
    }

    private void vncControl_ConnectionFailed(object sender, EventArgs e)
    {
    }

    private void tmrStatistics_Tick(object sender, EventArgs e)
    {
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string title = "RemoteViewing - Example VNC Client";

        Vnc.VncClientStatistics stats = vncControl.Client.GetStatistics();
        double recv = stats.BytesReceivedPerSecond;
        double send = stats.BytesSentPerSecond;
        int cpu = (int)Math.Round(stats.CpuUsage * 100);
        double fps = vncControl.CurrentFps;

        if (recv / 1024 >= 0.1 || send / 1024 >= 0.1 || cpu > 0 || fps > 0)
        {
            title += string.Format(" - {0} KB/s received, {1} KB/s sent, {2}% CPU, {3} FPS"
                , (recv / 1024).ToString("0.0")
                , (send / 1024).ToString("0.0")
                , cpu
                , fps.ToString("0")
                );
        }

        Text = title;
    }
}
