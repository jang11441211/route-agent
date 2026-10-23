# RouteAgent

**简体中文** · [English](README.md)

基于 [mihomo](https://github.com/MetaCubeX/mihomo) TUN 模式的 Windows 10/11 全局网卡分流工具。

按进程把流量分配到指定网卡：比如内网软件走有线网卡，浏览器等上网软件走手机 USB 共享网络。不需要手写 YAML，全部在图形界面里配置。

## 功能

- **按进程分流** — 把某个程序（如 `chrome.exe`）绑定到指定网卡。
- **从运行中的程序选择** — 可搜索的进程列表，带图标，支持一次勾选多个。
- **一键启停** — 自动生成 mihomo 配置并管理内核进程。
- **内核管理** — 从 GitHub 下载 mihomo，或导入包含 `mihomo.exe` 与 `wintun.dll` 的本地 zip。
- **实时日志** — 最多 1500 行并自动滚动；可暂停阅读，暂停期间的日志不会丢。
- **深浅主题** — 切换后立即生效并记住选择。
- **开机自启** — 可选随 Windows 启动，也可选启动后自动开启分流。
- **避免环路** — 本程序自己的 TUN 网卡不会出现在任何网卡下拉框中。

## 环境要求

| 项目 | 说明 |
| --- | --- |
| 系统 | Windows 10 / 11（x64） |
| 编译 | .NET 10 SDK |
| 权限 | 管理员权限（创建 TUN 网卡需要加载 wintun 驱动） |
| 内核 | `%LOCALAPPDATA%\RouteAgent\core` 下需有 `mihomo.exe` 和 `wintun.dll` |

## 快速开始

1. 编译并运行：

   ```powershell
   dotnet build -c Release
   .\bin\Release\net10.0-windows\RouteAgent.exe
   ```

2. 进入 **设置 → 内核**，在线下载 mihomo，或导入本地 zip。
3. 按提示以管理员身份重启应用（创建 TUN 网卡需要）。
4. 在 **规则** 页点 **添加规则**，选择程序、出口网卡并保存。
5. 点 **启动分流**，列表中的进程就会从各自指定的网卡出口。

## 工作原理

每次启动分流时，程序会生成 `%LOCALAPPDATA%\RouteAgent\config.yaml`：

- TUN 入站，开启 `auto-route` 与 DNS 劫持（fake-ip，`198.18.0.1/16`）；
- 每个被引用的网卡生成一个 `type: direct` 出站，用 `interface-name` 绑定物理网卡；
- 每条启用的规则生成 `PROCESS-NAME,<进程>.exe,<网卡标签>`；
- 末尾 `MATCH,DIRECT` 兜底。

mihomo 作为子进程运行；停止分流会结束进程并清理路由。分流运行期间修改规则，需要停止后重新启动分流才会生效。

## 数据位置

| 路径 | 用途 |
| --- | --- |
| `%LOCALAPPDATA%\RouteAgent\settings.json` | 规则、偏好设置、主题状态 |
| `%LOCALAPPDATA%\RouteAgent\config.yaml` | 自动生成的 mihomo 配置（每次启动重写） |
| `%LOCALAPPDATA%\RouteAgent\core\` | mihomo 内核与 `wintun.dll` |
| `%LOCALAPPDATA%\RouteAgent\routeagent.log` | 应用日志 |
| `%LOCALAPPDATA%\RouteAgent\core.log` | mihomo 输出 |

这些文件只保存在本机，并已被 `.gitignore` 排除。

## 从源码编译

```powershell
git clone git@github.com:jang11441211/route-agent.git
cd route-agent
dotnet build
dotnet run
```

技术栈：WPF + .NET 10（`net10.0-windows`），单工程，无第三方 NuGet 依赖。

## 说明与限制

- 一条规则只能选一张网卡；按进程名匹配，同名进程的所有实例都会命中。
- 同名可执行文件无法分别走不同网卡。
- 修改规则后需重启内核才会生效。
- 在线下载内核依赖 GitHub 连通性；访问不了时请用本地 zip 导入。

## 许可证

暂未添加许可证。计划对外分发或接受贡献前，建议先补一个（如 MIT）。
