# RouteAgent

[简体中文](README.zh-CN.md) · **English**

Per-app network adapter routing for Windows 10/11, powered by [mihomo](https://github.com/MetaCubeX/mihomo) in TUN mode.

Route each process through the network adapter you choose — for example, keep intranet tools on your wired LAN while browsers and other internet apps use a USB-tethered phone connection. No YAML editing required; everything is configured in a desktop UI.

## Features

- **Per-process rules** — bind a process (e.g. `chrome.exe`) to a specific network adapter.
- **Pick running processes** — searchable list of all running apps with icons; select several at once.
- **One-click start / stop** — the app writes the mihomo config and manages the core process for you.
- **Core management** — download mihomo from GitHub, or import a local zip containing `mihomo.exe` and `wintun.dll`.
- **Live logs** — up to 1500 lines with auto-scroll; pause the stream to read without missing anything.
- **Dark / light themes** — follows your choice, persisted between launches.
- **Start with Windows** — optional auto-start, and optional auto-start of routing.
- **No TUN loop** — the app's own TUN adapter is filtered out of every adapter picker.

## Requirements

| Item | Notes |
| --- | --- |
| OS | Windows 10 / 11 (x64) |
| Runtime (build) | .NET 10 SDK |
| Privileges | Administrator, to create the TUN adapter (wintun driver) |
| Core | `mihomo.exe` + `wintun.dll` in `%LOCALAPPDATA%\RouteAgent\core` |

## Quick start

1. Build and run:

   ```powershell
   dotnet build -c Release
   .\bin\Release\net10.0-windows\RouteAgent.exe
   ```

2. Open **设置 (Settings) → 内核 (Core)** and either download mihomo or import a local zip.
3. Click the administrator action to relaunch elevated — TUN creation needs it.
4. On **规则 (Rules)**, click **添加规则 (Add rule)**, pick a program, choose its exit adapter and save.
5. Click **启动分流 (Start)**. Traffic from the listed processes now leaves through their assigned adapters.

## How it works

`RouteAgent` generates a mihomo configuration on every start (`%LOCALAPPDATA%\RouteAgent\config.yaml`):

- a TUN inbound with `auto-route` and DNS hijack (fake-ip, `198.18.0.1/16`);
- one `type: direct` outbound per referenced adapter, with `interface-name` binding the traffic to that NIC;
- one `PROCESS-NAME,<process>.exe,<adapter-tag>` rule per enabled rule;
- a final `MATCH,DIRECT` fallback.

mihomo runs as a child process; stopping routing terminates it and removes the routes. Editing rules while routing is active requires a restart of routing (stop → start) to take effect.

## Data locations

| Path | Purpose |
| --- | --- |
| `%LOCALAPPDATA%\RouteAgent\settings.json` | Rules, preferences, window/theme state |
| `%LOCALAPPDATA%\RouteAgent\config.yaml` | Generated mihomo config (rewritten on start) |
| `%LOCALAPPDATA%\RouteAgent\core\` | mihomo core + `wintun.dll` |
| `%LOCALAPPDATA%\RouteAgent\routeagent.log` | Application log |
| `%LOCALAPPDATA%\RouteAgent\core.log` | mihomo output |

These files stay local and are excluded by `.gitignore`.

## Build from source

```powershell
git clone git@github.com:jang11441211/route-agent.git
cd route-agent
dotnet build
dotnet run
```

The project is WPF on .NET 10 (`net10.0-windows`), single project, no external NuGet dependencies.

## Notes & limitations

- One adapter per rule; a process name matches all instances of that executable.
- Two processes that share an executable name cannot be split onto different adapters.
- The core must be restarted for rule changes to apply.
- Downloading the core needs access to GitHub; if that is blocked, use the local zip import.

## License

No license has been added yet. Add one (e.g. MIT) before distributing or accepting contributions.
