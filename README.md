<p align="center">
  <img src="logo.png" alt="RemoteViewing Logo" width="128" height="128">
</p>

<h1 align="center">RemoteViewing</h1>

<p align="center">
  <strong>🖥️ A .NET-native VNC client and server library 🖥️</strong>
</p>

<p align="center">
  It supports Raw, Hextile, Copyrect, and Zlib encodings, and includes a Windows Forms and WPF control to make embedding VNC in your program extremely easy.
</p>

<p align="center">
  <a href="#-features">✨ Features</a> •
  <a href="#-installation">📦 Installation</a> •
  <a href="#-usage">🚀 Usage</a> •
  <a href="#-supported-frameworks">🎯 Frameworks</a> •
  <a href="#-license">📄 License</a>
</p>

---

## ✨ Features

- 🔷 **Pure .NET Implementation** - No native dependencies required
- 🔄 **VNC Client & Server** - Full support for both client and server roles
- 📦 **Multiple Encodings** - Raw, Hextile, Copyrect, and Zlib compression
- 🎨 **Color Depth Support** - 8-bit, 16-bit, and 32-bit color modes
- 🪟 **UI Controls** - Ready-to-use controls for Windows Forms and WPF
- 📋 **Clipboard Sharing** - Bidirectional clipboard synchronization
- 📊 **Performance Monitoring** - Built-in FPS, bandwidth, and CPU statistics
- 🔍 **AutoSize Mode** - Automatic scaling with proper coordinate transformation

## 📦 Installation

Install via NuGet Package Manager:

```bash
# Core library
dotnet add package RemoteViewing

# Windows Forms control
dotnet add package RemoteViewing.Windows.Forms

# WPF control
dotnet add package RemoteViewing.WPF
```

## 🚀 Usage

### 💻 VNC Client (Windows Forms)

```csharp
using RemoteViewing.Vnc;
using RemoteViewing.Windows.Forms;

// Add VncControl to your form, then connect:
var options = new VncClientConnectOptions();
options.Password = "your-password".ToCharArray();

vncControl.Client.Connect("hostname", 5900, options);
```

### 🪟 VNC Client (WPF)

```csharp
using RemoteViewing.Vnc;
using RemoteViewing.WPF;

// Add VncControl to your window, then connect:
var options = new VncClientConnectOptions();
options.Password = "your-password".ToCharArray();

vncControl.Client.Connect("hostname", 5900, options);
```

### 🖧 VNC Server

```csharp
using System.Net;
using System.Net.Sockets;
using RemoteViewing.Vnc;
using RemoteViewing.Vnc.Server;
using RemoteViewing.Windows.Forms.Server;

// Listen for connections
var listener = new TcpListener(IPAddress.Any, 5900);
listener.Start();

var client = listener.AcceptTcpClient();

// Configure server options
var options = new VncServerSessionOptions();
options.AuthenticationMethod = AuthenticationMethod.Password;

// Create and start session
var session = new VncServerSession();
session.PasswordProvided += (s, e) => e.Accept("password".ToCharArray());
session.SetFramebufferSource(new VncScreenFramebufferSource("Desktop", Screen.PrimaryScreen));
session.Connect(client.GetStream(), options);
```

## 🎯 Supported Frameworks

| Package | Supported Frameworks |
|---------|---------------------|
| RemoteViewing | .NET Framework 4.6.2-4.8, .NET Standard 2.0/2.1, .NET 5.0-10.0 |
| RemoteViewing.Windows.Forms | .NET Framework 4.6.2-4.8, .NET 5.0-10.0 (Windows) |
| RemoteViewing.WPF | .NET Framework 4.6.2-4.8, .NET 5.0-10.0 (Windows) |

## 📡 VNC Protocol Support

| Feature | Client | Server |
|---------|--------|--------|
| Raw Encoding | ✅ | ✅ |
| Hextile Encoding | ✅ | ✅ |
| Copyrect Encoding | ✅ | ✅ |
| Zlib Encoding | ✅ | ✅ |
| Password Authentication | ✅ | ✅ |
| Clipboard Sharing | ✅ | ✅ |
| 8-bit Color | ✅ | ✅ |
| 16-bit Color | ✅ | ✅ |
| 32-bit Color | ✅ | ✅ |

## 📄 License

This project is licensed under the BSD 2-Clause License. See [LICENSE.txt](LICENSE.txt) for details.

## 🙏 Acknowledgments

- 👨‍💻 Original author: [James F. Bellinger](http://software.seekye.com/remoteviewing)
- 📚 zlib compression support uses a C# port of zlib's deflate code
