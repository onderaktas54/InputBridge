# InputBridge on Linux 🐧

`InputBridge.Linux` is a headless (console) edition that brings InputBridge to Linux.
It **reuses the exact same `InputBridge.Core`** networking, discovery, handshake and
AES-256-GCM encryption as the Windows apps, so a Linux machine interoperates with a
Windows Host/Client over the identical protocol.

The Linux-specific part is only *how input is captured and injected*:

| Concern | Windows | Linux |
|---|---|---|
| Inject input (client) | Win32 `SendInput` | `/dev/uinput` (virtual device) |
| Capture input (host) | Win32 low-level hooks | evdev (`/dev/input/event*`) |

Because uinput/evdev live at the **kernel** layer, this works under **both X11 and
Wayland** — unlike the X11-only `XTEST` approach.

## Two modes

- **client** — let another machine (Windows or Linux Host) control *this* Linux box.
  Received events are injected via `/dev/uinput`.
- **host** — control another machine *from* this Linux box. Local keyboard/mouse are
  captured via evdev and streamed to the connected client.

## Build

Requires the .NET 8 SDK (or newer with roll-forward).

```bash
dotnet build src/InputBridge.Linux/InputBridge.Linux.csproj -c Release
```

The binary lands at `src/InputBridge.Linux/bin/Release/net8.0/inputbridge-linux`.

> Building the whole `InputBridge.sln` on Linux will fail on the WPF (`net8.0-windows`)
> projects — that is expected. Build just the `InputBridge.Linux` project on Linux.

## Run

Both modes need access to kernel input nodes, so either run with `sudo` or install the
udev rule below.

```bash
# Be controlled by a Host (auto-discovers it on the LAN):
sudo ./inputbridge-linux client --secret mypass

# ...or connect straight to a known Host IP:
sudo ./inputbridge-linux client --host 192.168.1.20 --secret mypass

# Control another machine from here:
sudo ./inputbridge-linux host --secret mypass
```

### Host hotkeys
- **Ctrl+Alt+S** — toggle forwarding on/off. While ON, your keyboard/mouse are
  exclusively grabbed and go to the *client* instead of this machine.
- **Ctrl+Alt+Esc** — emergency release (stop forwarding immediately).

### Options
| Option | Meaning |
|---|---|
| `--secret <text>` | Shared secret; **must match** the other side. |
| `--host <ip>` | (client) Connect directly, skip LAN discovery. |
| `--port <n>` | TCP port (default `7201`). UDP uses `port-1`. |

## Running without sudo (udev rule)

Install the provided rule so the `input` group can use uinput/evdev:

```bash
sudo cp packaging/99-inputbridge.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules && sudo udevadm trigger
sudo usermod -aG input "$USER"   # then log out and back in
sudo modprobe uinput             # ensure the module is loaded
```

After that, `./inputbridge-linux client ...` works as your normal user.

## Interop notes
- Keyboard events travel over TCP (reliable); mouse movement/scroll over UDP (low latency).
- Windows Virtual-Key codes on the wire are mapped to/from Linux evdev key codes
  (`KeyMap.cs`). The mapping targets a US layout; non-US layouts may need additions.
- On a **single machine** you cannot run host and client together (both bind UDP
  `port-1`) — that conflict only appears in loopback testing, not across two machines.

## Status
Client (injection) and Host (capture) are functional. This is a new port — please file
issues for any key that isn't mapped or any device that isn't detected.
