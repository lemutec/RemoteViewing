1.2.0 (2026-01-26):
  Added a new WPF library (RemoteViewing.WPF) with full VncControl implementation.
  Redesigned AutoSize mode rendering to use ScaleTransform for efficient scaling without resizing the control.
  Added coordinate transformation system for proper mouse input handling in AutoSize mode.
  Added a custom dot cursor (black center with white border) for better visibility when hovering over the VNC control.
  Added FPS (frames per second) tracking to VncControl in both Windows Forms and WPF libraries.
  Added ShowFps property to display FPS overlay in the top-left corner of the VNC control.
  Added CurrentFps read-only property to programmatically access the current frame rate.
  Added auto-reconnect support (Note: This feature is experimental and not stable).
  Fixed zlib stream memory growth in framebuffer decoder.
  Example applications now display FPS in the title bar alongside bandwidth and CPU statistics.

1.1.0 (2025-10-14):
  Added support for 8-bit color (2-3-3 RGB) to the VNC server. It's not pretty, but it is bandwidth-efficient.
  Added Zlib support to the VNC server. Previously RemoteViewing was only useful on a LAN. With this change, it should be useful over the Internet.
  Fixed an occasional crash on exit in the Windows Forms VNC control.
  On 64-bit platforms, the VNC server is now another 25% faster when the desktop is idle.
  The VNC server now uses a steady amount of RAM, even when updating the screen quickly. As a result, the garbage collector no longer causes CPU spikes.

1.0.1 (2025-10-09):
  Added (very) basic Hextile support to the VNC server.
  Fixed a lock-up in the VNC server.
  The VNC server is now 2.5X faster at updating frames.
  Started on bandwidth statistics support. This is not very accurate yet.

1.0.0 (2025-09-24):
  Added support for 16-bit color.
  Fixed mouse coordinate scaling on the Windows Forms control.

0.9.4 (2018-11-11):
  Fixed the VNC server behavior when an incorrect password is supplied.

0.9.3 (2017-11-02):
  Added support for palettized color and 32-bit color.

0.9.2 (2016-10-23):
  Added a SizeMode property to the Windows Forms control.

0.9.1 (2013-05-11):
  Added a preliminary VNC server implementation.
  Added clipboard sharing support.
  The underlying client is now free-threaded, and the Windows Forms control handles marshaling.
  The download at http://software.seekye.com/remoteviewing includes samples if you need them.

0.9.0 (2013-04-22):
  Test version. Still needs cleanup and better keyboard handling.
