# MARS (Modular API Remote System)

MARS is a cross-platform, highly extensible remote administration and system monitoring framework. It features a self-contained core architecture powered by a Python backend and a dynamic C# Avalonia-based client interface.

Unlike traditional monolithic remote administration tools, MARS uses a "Convention over Configuration" modular approach. The server core acts as a routing and module-loading engine, allowing administrators to easily add or remove Python-based modules without recompiling the application. The C# client dynamically generates its UI based on the JSON schemas provided by the active modules on the server.

## Platform Compatibility

- **Windows:** Windows 10, Windows 11 (x64)
- **Linux:** Debian and Ubuntu-based distributions (amd64)

## Dependencies & Architecture

The MARS core engine is **self-contained**. It utilizes an embedded, portable Python environment that is extracted during the build process, meaning the host system does not need Python pre-installed.

### Modular Dependencies
While the core requires no external dependencies, specific hardware-interacting modules may rely on system-level tools:
- **FFmpeg:** Highly recommended. Required by the `remote_control` module for capturing and encoding h.264 video streams, and required by the Client to decode the incoming stream.
- **xclip / xsel / wl-clipboard:** Required on Linux for the `clipboard_sync` module.

*Note: The official installers automatically handle FFmpeg. Linux-specific clipboard tools (xclip/xsel/wl-clipboard) must be installed manually if the clipboard synchronization module is used.*

## Installation

You can download the latest release from the [Releases](../../releases) page.

### 1. Recommended Method (Installers)
The easiest way to deploy MARS. The installers automatically register the background daemon as a system service, update system `PATH` variables, and bundle core dependencies like FFmpeg.

- **Windows (`.exe`):** Download `MARS_Setup_v1.0.0.exe` and run it. It will automatically bundle FFmpeg and register the `MarsDaemon` Windows Service.
- **Linux (`.deb`):** Download `mars-remote-system_1.0.0_amd64.deb` and run:
  ```bash
  sudo apt install ./mars-remote-system_1.0.0_amd64.deb
  ```
  This package will automatically resolve and install ffmpeg via apt, set executable permissions, and configure the systemd daemon.

### 2. Manual Setup (Raw Binaries)
For advanced users who prefer standalone binaries without automated installation. Download `MARS_v1.0.0_Windows_Binaries.zip` or `MARS_v1.0.0_Linux_Binaries.tar.gz`.

**Requirements for raw binaries:**
- You must manually execute the daemon executable with Administrator/Root privileges.
- On Linux, you must manually grant execution permissions (`chmod +x`) to the C# binaries.
- You must manually install ffmpeg and ensure it is available in your system's PATH for both the server machine and the client machine.

## MARS Hub (Module Marketplace)

MARS features an official [Module Marketplace (MARS Hub)](https://github.com/TheVekT/mars-hub) where you can discover and install additional modules to extend the system's capabilities.

- **Direct Installation:** Browse and install modules directly from the MARS UI.
- **Contribute:** Add your own Python-based modules to the marketplace to share with others.

## Building from Source
To keep the git repository lightweight, the large portable Python runtimes are excluded from version control. You must download them manually before building the project.

1. **Clone the repository:**
   ```bash
   git clone https://github.com/TheVekT/mars.git
   cd mars
   ```

2. **Download the Python Runtimes** (`win64.zip` and `linux.tar.gz`) from the [Releases](../../releases) page.

3. **Place both archives** directly into the `/python-runtime/` directory. Do not extract them; the MSBuild pipeline handles extraction automatically.

4. **Build the solution** using the .NET 8 SDK:

   **For Windows:**
   ```bash
   dotnet publish Mars.sln -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false
   ```

   **For Linux:**
   ```bash
   dotnet publish Mars.sln -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=false
   ```

The compiled output, including the bundled Python runtime and server code, will be located in `Build/Release/<runtime>/`.

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE.txt) file for details.
