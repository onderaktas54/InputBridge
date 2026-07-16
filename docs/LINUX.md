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

Release archives are self-contained and do not require a system-wide .NET runtime. To
build from source, install the .NET 8 SDK or newer.

```bash
dotnet publish src/InputBridge.Linux/InputBridge.Linux.csproj -c Release -r linux-x64 --self-contained
```

The self-contained binary lands under
`src/InputBridge.Linux/bin/Release/net8.0/linux-x64/publish/inputbridge-linux`.

> Building the whole `InputBridge.sln` on Linux will fail on the WPF (`net8.0-windows`)
> projects — that is expected. Build just the `InputBridge.Linux` project on Linux.

## Run

Both modes need access to kernel input nodes. Install the udev rule below instead of
running the network-facing application as root.

```bash
# Be controlled by a Host (auto-discovers it on the LAN):
INPUTBRIDGE_SECRET='use-a-long-random-secret' ./inputbridge-linux client

# ...or connect straight to a known Host IP:
INPUTBRIDGE_SECRET='use-a-long-random-secret' ./inputbridge-linux client --host 192.168.1.20

# Control another machine from here:
INPUTBRIDGE_SECRET='use-a-long-random-secret' ./inputbridge-linux host
```

### Host hotkeys
- **Ctrl+Alt+S** — toggle forwarding on/off. While ON, your keyboard/mouse are
  exclusively grabbed and go to the *client* instead of this machine.
- **Ctrl+Alt+Esc** — emergency release (stop forwarding immediately).

### Options
| Option | Meaning |
|---|---|
| `--secret <text>` | Shared secret; must match and be at least 16 characters. Prefer `INPUTBRIDGE_SECRET` so it is not stored in shell history or exposed in the process command line. |
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

Membership in the `input` group permits reading physical input devices and creating
virtual ones. Only grant it to trusted local users. Use a long, unique shared secret;
the application intentionally refuses to start with the old public default secret.

## Interop notes
- Keyboard events travel over TCP (reliable); mouse movement/scroll over UDP (low latency).
- Windows Virtual-Key codes on the wire are mapped to/from Linux evdev key codes
  (`KeyMap.cs`). The mapping targets a US layout; non-US layouts may need additions.
- On a **single machine** you cannot run host and client together (both bind UDP
  `port-1`) — that conflict only appears in loopback testing, not across two machines.

## Status
Client (injection) and Host (capture) are functional. This is a new port — please file
issues for any key that isn't mapped or any device that isn't detected.
