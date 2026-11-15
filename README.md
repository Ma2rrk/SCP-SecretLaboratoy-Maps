# SCP: Secret Laboratory Maps

SCP: Secret Laboratory 的地图覆盖层和本地地图生成器。程序从游戏日志读取地图种子，自动获取或生成地图，并在游戏上方显示房间、连接和中文房间名称。

SCP: Secret Laboratory map overlay and local map generator. The overlay reads the map seed from the game log, loads or generates the map, and displays rooms, connections, and Chinese room names above the game.

## 功能 Features

- 自动发现 `Player.log`、读取最新地图种子并刷新地图。
- 支持主 API、备用 API 和本地生成三种地图来源，新配置默认本地生成。
- 根据游戏资源中的 atlas、房间 prefab、权重和旧版 `System.Random` 复现本地地图生成。
- 预存 atlas 房间连接，绘制去重后的房间连接线。
- EZ、HCZ 和 LCZ 使用独立的缩放与位置设置。
- 设置面板支持地图透明度、房间颜色和连接颜色自定义。
- 切换获取种子键时，从内置映射文本读取键码并复制游戏控制台绑定命令，兼容可选的外部 BAT。
- 地图字体支持已安装字体，切换后立即生效。
- 使用 `room.txt` 提供中文房间名称；没有中文翻译的英文房间名不会显示。
- `MicroHID` 在界面中显示为 `HID`。

- Automatically discovers `Player.log`, reads the latest map seed, and refreshes the map.
- Supports primary API, backup API, and local generation sources; new configurations default to local generation.
- Reproduces local generation from game atlas data, room prefabs, weights, and the legacy `System.Random` algorithm.
- Stores atlas connections in advance and draws deduplicated room connections.
- Provides independent scale and position settings for EZ/HCZ and LCZ.
- Lets you customize map opacity, room color, and connection color from the settings panel.
- Reads embedded key mapping data and copies a console binding command when the seed key changes, with optional external BAT support.
- Supports installed map fonts, with immediate switching.
- Uses `room.txt` for Chinese room names; untranslated English room names are hidden.
- Displays `MicroHID` as `HID` in the UI.

## 运行要求 Requirements

- Windows 10/11。
- 已安装 SCP: Secret Laboratory，并允许程序读取游戏日志。
- 使用 Windows x64 自包含发布包时无需安装 .NET；从源码编译需要 .NET 10 SDK。
- 游戏运行时通常会将日志写入：
  `%USERPROFILE%\AppData\LocalLow\Northwood\SCPSL\Player.log`
- `Python 3.10+` 和 `UnityPy` 仅在重新提取游戏资源或重建研究数据时需要。

- Windows 10/11.
- SCP: Secret Laboratory installed, with permission to read its log files.
- The Windows x64 self-contained release requires no separate .NET installation. Building from source requires the .NET 10 SDK.
- The game normally writes its log to:
  `%USERPROFILE%\AppData\LocalLow\Northwood\SCPSL\Player.log`
- `Python 3.10+` and `UnityPy` are only needed when extracting assets or rebuilding research data.

## 安装与启动 Installation

发布包用户：将整个 ZIP 解压到可写目录，启动 `SLMapsOverlay.exe`。保留程序旁的两份地图数据、`room.txt` 和默认配置；按键映射已内置，发布包无需附带 BAT。按 `F8` 打开设置，在“地图来源”下方切换地图版本。15.0 Beta 的 EZ 仍有 12 个待游戏实测确认的端口，相关连线尚不完整。

Release users: extract the entire ZIP into a writable folder and launch `SLMapsOverlay.exe`. Keep both map data files, `room.txt` and the default settings alongside it. Key mappings are embedded; no BAT is included or required. Press `F8` to open settings and switch map versions below the map source option. The 15.0 Beta EZ connections remain incomplete at 12 ports pending comparison with in-game maps.

从源码运行 / Run from source:

```powershell
git clone https://github.com/Ma2rrk/SCP-SecretLaboratoy-Maps.git
cd SCP-SecretLaboratoy-Maps
dotnet run --project SLMapsOverlay.csproj
```

也可以先构建再运行：

```powershell
dotnet build SLMapsOverlay.csproj -c Release
dotnet run --project SLMapsOverlay.csproj -c Release
```

You can also build and run the release configuration:

```powershell
dotnet build SLMapsOverlay.csproj -c Release
dotnet run --project SLMapsOverlay.csproj -c Release
```

建议先启动 SCP: Secret Laboratory，再启动覆盖层。覆盖层会保持置顶，但不会替代游戏窗口。

Start SCP: Secret Laboratory before starting the overlay. The overlay stays on top of the game and does not replace the game window.

维护者可通过以下命令生成自包含发布包（Windows x64），ZIP、SHA-256 和源提交信息写入 `artifacts/`：

Maintainers can build a self-contained Windows x64 ZIP with the following command. The archive, SHA-256 and source revision information are written to `artifacts/`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Release.ps1
```

## 使用地图 Using the Map

1. 启动游戏并进入一局，使游戏日志中出现地图种子。
2. 启动覆盖层。程序会自动查找日志并在检测到新种子后加载地图。
3. 如果需要立即读取种子，确认游戏窗口处于焦点状态，然后按 `PageDown`。
4. 按 `F8` 显示或隐藏设置面板。
5. 用鼠标滚轮缩放鼠标所在区域；EZ/HCZ 和 LCZ 分别保存缩放比例。
   缩放会同步对应区域的控制滑块；面板区域不会额外触发地图滚轮缩放。
6. 在地图区域按住鼠标左键拖动，可分别移动 EZ/HCZ 或 LCZ 地图。
7. 房间名称、地图连接和地图样式会在地图刷新后保留。

1. Start the game and enter a round so the map seed is written to the log.
2. Start the overlay. It searches the log automatically and loads a map when a new seed is detected.
3. To read the seed immediately, focus the game window and press `PageDown`.
4. Press `F8` to show or hide the settings panel.
5. Use the mouse wheel to zoom the region under the cursor. EZ/HCZ and LCZ zoom values are stored separately.
6. Hold the left mouse button in a map region to move that region.
7. Room names, connections, and map styling are preserved when the map refreshes.

## 设置面板 Settings Panel

设置面板是可滚动页面，修改后点击“保存设置”写入配置。
拖动面板标题或副标题可以移动面板，松开后自动保存位置；下次启动会恢复，并限制在窗口内。

The settings panel is scrollable. Click **Save Settings** after making changes.
Drag the title or subtitle to move the panel. Its position is saved when released and restored within the window on startup.

### 地图布局 Map Layout

- 核心缩放 / Core scale：调整 EZ 和 HCZ 的地图大小。
- 核心 X、核心 Y / Core X and Core Y：调整 EZ/HCZ 地图位置。
- LCZ 缩放、LCZ X、LCZ Y / LCZ scale, LCZ X, and LCZ Y：独立调整 LCZ 地图。

### 地图样式 Map Style

- 地图透明度 / Map opacity：在 `10%` 到 `100%` 之间调整房间、连接线、边框和名称的透明度。
- 房间颜色 / Room color：点击颜色方块打开系统取色器。
- 连接颜色 / Connection color：单独设置房间连接线颜色。
- 地图字体 / Map font：选择已安装字体，地图立即重绘，不显示切换提示。已有字体文件配置仍可读取，字体不可用时回退系统字体。
- 地图字号 / Map font size：默认“自动（随地图缩放）”，也可选择 `6–48 pt` 的固定字号。修改立即生效并自动保存，固定字号统一应用于核心区和 LCZ。
- 字体加粗 / Bold：默认开启，可切换常规或粗体；字体没有对应样式时使用该字体可用的样式。
- 启用 ClearType / Enable ClearType：默认关闭，开启后使用 ClearType 渲染，关闭后使用灰度抗锯齿。选项立即生效并自动保存；透明覆盖层上可能出现彩色边缘，可按实际效果选择。

### 快捷键与地图来源 Hotkeys and Map Source

- 面板快捷键 / Settings panel hotkey：默认 `F8`。
- 获取种子键 / Seed key：默认 `PageDown`。程序只会在游戏窗口处于前台时模拟该按键。
- 修改获取种子键后，对应的 `cmdbind <游戏键码> seed` 命令自动复制到剪贴板，请粘贴到游戏控制台执行。也可以点击“复制绑定命令”重试。默认读取内置映射文本；若自行在程序旁放置 `scpsl-seed-detector.bat`，则优先解析该文件。覆盖层不会执行 BAT 或自动修改游戏绑定文件。
- Changing the seed key copies its `cmdbind <game key code> seed` command. Paste it into the game console to apply the binding. Embedded mappings are used by default; an optional `scpsl-seed-detector.bat` alongside the executable overrides them. The overlay never executes the BAT or edits game bindings.
- 地图来源 / Map source：
  - `主 API`：使用主地图 API。
  - `备用 API (slmaps.com)`：使用备用 API。
  - `本地生成`：默认选项，不请求地图 API，使用仓库中的 `map-generation-data.json` 本地生成。保留已有配置明确选择的来源。
- 本地生成时可在设置面板切换地图版本：勾选 `15.0 Beta` 使用新版数据，取消勾选使用随程序发布的 `14.2.9` 数据；切换会重载当前种子。
- For local generation, enable `15.0 Beta` to use the new map data or disable it to use `14.2.9`. Switching reloads the current seed and saves the selection. API responses are controlled by their servers.

## 配置文件 Configuration

用户设置保存在：

```text
%LOCALAPPDATA%\SLMapsOverlay\overlay.json
```

其中包括窗口位置、设置面板位置（`ControlPanelX` / `ControlPanelY`）、区域缩放与偏移、地图透明度、房间颜色、连接颜色、快捷键、地图来源、地图版本（`UseBetaMapGeneration`）及地图字体（`MapFontFamily` / `MapFontFile` / `MapFontSize`，字号为 `0` 表示自动），以及加粗（`MapFontBold`）和 ClearType（`UseClearType`）选项。删除该文件会恢复默认设置。

User settings are stored at:

```text
%LOCALAPPDATA%\SLMapsOverlay\overlay.json
```

The file contains window and settings panel placement, region scale and offsets, map opacity, colors, hotkeys, map source, map version (`UseBetaMapGeneration`), and map font settings. A font size of `0` follows map zoom; fixed sizes range from `6` to `48 pt`. Existing font file settings remain readable, with a system font fallback when unavailable. Delete the settings file to restore the defaults.

## 本地地图生成 Local Generation

本地生成器使用 `map-generation-data.json`（15.0 Beta）或 `map-generation-data-14.2.9.json`（14.2.9）。两份数据都包含对应版本的 atlas 网格、允许的旋转、固定房间、连接方向、房间 prefab 限制和显示字段；Beta 数据还包含 prefab 连接器统计。

The local generator uses `map-generation-data.json` (15.0 Beta) or `map-generation-data-14.2.9.json` (14.2.9). Each file contains the matching atlas grids, allowed rotations, fixed rooms, connection directions, prefab limits, and display metadata.

本地生成的流程为：按 seed 选择各区域 atlas，选择旋转与房间顺序，按 prefab 最小/最大数量和权重分配房间，然后对 EZ/HCZ 检查点进行对齐。房间连接来自固定 atlas 结构，具体房间身份由 seed 决定。

The local generation flow selects an atlas per zone from the seed, chooses rotations and cell order, assigns prefabs using minimum/maximum counts and weights, and aligns the EZ/HCZ checkpoints. Connections come from the fixed atlas structure while room identities vary with the seed.

本地数据与提取数据对应的游戏版本有关。游戏更新后，可能需要重新提取资源并重建 `map-generation-data.json`。

The local data is tied to the game version from which it was extracted. After a game update, the asset data may need to be regenerated.

## 研究与验证 Research and Verification

研究脚本位于 `research/`。重新提取资源需要从游戏目录读取 `SCPSL_Data/level2` 和 `sharedassets2.assets`，并使用 UnityPy：

The research scripts are in `research/`. Asset extraction reads `SCPSL_Data/level2` and `sharedassets2.assets` from the game directory and requires UnityPy:

```powershell
python research/extract_map_assets.py
python research/build_generation_data.py
python research/verify_generation.py
dotnet run --project research/MapGeneratorVerification.csproj --no-restore
```

验证程序会校验 Beta 资源指纹、atlas 格子和连接互反性，并检查 Gate A/B 的多连接器元数据；历史 API 样本只用于旧版本回归。当前没有新版真实游戏同种子导出，静态资源验证不能替代运行时对照。

Verification covers Beta resource fingerprints, atlas cells, reciprocal connections, connector metadata and deterministic generation. Historical API fixtures provide regression checks for the older version only. No known-seed 15.0 Beta in-game export has been compared yet.

## 故障排查 Troubleshooting

- 没有地图：确认游戏已经进入一局，并检查 `Player.log` 是否存在新的 seed 记录。
- 种子没有更新：确认游戏窗口处于前台，按 `PageDown`，或等待日志轮询刷新。
- 本地生成失败：确认 `map-generation-data.json` 和 `map-generation-data-14.2.9.json` 位于程序目录。从源码编译时还需安装 .NET 10 SDK。
- 名称为空：程序会隐藏没有中文翻译的英文标识；可在 `room.txt` 中补充翻译后重启程序。
- 地图位置不合适：打开设置面板分别调整核心区域和 LCZ 的缩放、X 偏移、Y 偏移。

- No map: make sure a round has started and that `Player.log` contains a new seed.
- Seed does not update: focus the game window, press `PageDown`, or wait for log polling.
- Local generation fails: make sure both generation data files are beside the executable. Building from source also requires the .NET 10 SDK.
- Missing names: untranslated English identifiers are intentionally hidden; add a translation to `room.txt` and restart the program.
- Incorrect map position: open the settings panel and adjust the core and LCZ scale and offsets separately.

## 许可证与数据来源 License and Data Sources

地图结构研究参考了公开的逆向分析文章和地图 API：

The map structure research references the following public reverse-engineering article and map API:

- [地图生成算法逆向分析 / Map generation reverse engineering](https://fxdyj.com/archives/scpsl-mapgen-reverse-engineering)
- [SCP:SL Maps API](https://scpslmaps.fxdyj.com/)

游戏资源只用于本地只读分析和验证；本仓库不包含游戏程序集或原始 Unity 资源。

Game assets are used only for local read-only analysis and verification. This repository does not include the game assembly or original Unity assets.



哦对了 上面是AI写的 我没仔细看
