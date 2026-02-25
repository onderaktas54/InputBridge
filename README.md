# InputBridge

![Status](https://img.shields.io/badge/Status-Beta-brightgreen.svg)
![Platform](https://img.shields.io/badge/Platform-Windows%20WPF-blue.svg)
![Security](https://img.shields.io/badge/Security-AES--256--GCM-red.svg)

**InputBridge** is a low-latency, secure network-based KVM software designed to share your mouse and keyboard across multiple PCs on the same LAN without the need for a physical hardware switch or extra cables.

---

## 🚀 Features

*   **Zero-Allocation Network Stack:** Utilizes raw IP packets over UDP and TCP to guarantee lightning-fast inputs (usually sub-1ms on LAN).
*   **Security First:** All inputs are encrypted end-to-end using AES-256-GCM. An eavesdropper on the network cannot record your keystrokes.
*   **Device Discovery:** Hosts broadcast their presence securely. Clients can automatically discover and attempt handshakes avoiding painful IP entries.
*   **WPF Modern UI:** Simple dark-mode interface with a system tray background service.
*   **Customizable Hotkeys:** Quickly switch between controlling your physical (Local) machine and your remote (Client) machine.

## 🛠 Architecture

InputBridge consists of three main components:
1.  **InputBridge.Host:** Runs on the computer with the physical mouse and keyboard. Uses low-level Windows Hooks (`SetWindowsHookEx`) to intercept hardware inputs and route them over the network.
2.  **InputBridge.Client:** Runs on the remote computer you wish to control. Uses native Windows API (`SendInput`) to simulate the received keystrokes and mouse movements.
3.  **InputBridge.Core:** Shared networking, cryptography, and logic layer.

## 📥 Installation

*Binary releases will be available in the [Releases](#) tab soon.*

1.  Download the latest `InputBridge_Release.zip` for Host and Client.
2.  Extract the folder.
3.  Run `InputBridge.Host.exe` on your primary computer.
4.  Run `InputBridge.Client.exe` on your secondary computer.

### Configuration
On both machines, ensure you enter the **exact same Secret Key** and use the same **TCP Port**. 
Once connected, a green "Connected" status will appear.

To switch input to the second computer: Press `Ctrl+Win+1` or `Ctrl+Win+2` (customizable in settings).
To emergency release input: Press `Ctrl+Alt+Escape`.

## 🖥 Building from Source

To build InputBridge yourself, you need the .NET 8 SDK or later installed.
```cmd
git clone https://github.com/yourusername/InputBridge.git
cd InputBridge
dotnet build InputBridge.sln -c Release
```

## 📄 License
This open-source project is licensed under the MIT License.
