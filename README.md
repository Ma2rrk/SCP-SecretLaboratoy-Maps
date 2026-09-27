# SCP: Secret Laboratory Maps

SCP: Secret Laboratory 的地图覆盖层和本地地图生成器。程序从游戏日志读取地图种子，自动获取或生成地图，并在游戏上方显示房间、连接和中文房间名称。

SCP: Secret Laboratory map overlay and local map generator. The overlay reads the map seed from the game log, loads or generates the map, and displays rooms, connections, and Chinese room names above the game.

## 功能 Features

- 自动发现 `Player.log`、读取最新地图种子并刷新地图。
- 支持 Primary API、Backup API 和 Local generation 三种地图来源。
- 根据游戏资源中的 atlas、房间 prefab、权重和旧版 `System.Random` 复现本地地图生成。
- 预存 atlas 房间连接，绘制去重后的房间连接线。
- EZ、HCZ 和 LCZ 使用独立的缩放与位置设置。
- 设置面板支持地图透明度、房间颜色和连接颜色自定义。
- 使用 `room.txt` 提供中文房间名称；没有中文翻译的英文房间名不会显示。
- `MicroHID` 在界面中显示为 `HID`。

- Automatically discovers `Player.log`, reads the latest map seed, and refreshes the map.
- Supports Primary API, Backup API, and Local generation sources.
- Reproduces local generation from game atlas data, room prefabs, weights, and the legacy `System.Random` algorithm.
- Stores atlas connections in advance and draws deduplicated room connections.
- Provides independent scale and position settings for EZ/HCZ and LCZ.
- Lets you customize map opacity, room color, and connection color from the settings panel.
- Uses `room.txt` for Chinese room names; untranslated English room names are hidden.
- Displays `MicroHID` as `HID` in the UI.

## 运行要求 Requirements

- Windows 10/11。
- 已安装 SCP: Secret Laboratory，并允许程序读取游戏日志。
- .NET 10 SDK，用于编译和运行 WinForms 覆盖层。
- 游戏运行时通常会将日志写入：
  `%USERPROFILE%\AppData\LocalLow\Northwood\SCPSL\Player.log`
- `Python 3.10+` 和 `UnityPy` 仅在重新提取游戏资源或重建研究数据时需要。

- Windows 10/11.
- SCP: Secret Laboratory installed, with permission to read its log files.
- .NET 10 SDK for building and running the WinForms overlay.
- The game normally writes its log to:
  `%USERPROFILE%\AppData\LocalLow\Northwood\SCPSL\Player.log`
- `Python 3.10+` and `UnityPy` are only needed when extracting assets or rebuilding research data.

## 安装与启动 Installation

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

## 使用地图 Using the Map

1. 启动游戏并进入一局，使游戏日志中出现地图种子。
2. 启动覆盖层。程序会自动查找日志并在检测到新种子后加载地图。
3. 如果需要立即读取种子，确认游戏窗口处于焦点状态，然后按 `PageDown`。
4. 按 `F8` 显示或隐藏设置面板。
5. 用鼠标滚轮缩放鼠标所在区域；EZ/HCZ 和 LCZ 分别保存缩放比例。
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

The settings panel is scrollable. Click **Save Settings** after making changes.

### 地图布局 Map Layout

- 核心缩放 / Core scale：调整 EZ 和 HCZ 的地图大小。
- 核心 X、核心 Y / Core X and Core Y：调整 EZ/HCZ 地图位置。
- LCZ 缩放、LCZ X、LCZ Y / LCZ scale, LCZ X, and LCZ Y：独立调整 LCZ 地图。

### 地图样式 Map Style

- 地图透明度 / Map opacity：在 `10%` 到 `100%` 之间调整房间、连接线、边框和名称的透明度。
- 房间颜色 / Room color：点击颜色方块打开系统取色器。
- 连接颜色 / Connection color：单独设置房间连接线颜色。

### 快捷键与地图来源 Hotkeys and Map Source

- 面板快捷键 / Settings panel hotkey：默认 `F8`。
- 获取种子键 / Seed key：默认 `PageDown`。程序只会在游戏窗口处于前台时模拟该按键。
- 地图来源 / Map source：
  - `Primary API`：使用主地图 API。
  - `Backup API (slmaps.com)`：使用备用 API。
  - `Local generation`：不请求地图 API，使用仓库中的 `map-generation-data.json` 本地生成。

## 配置文件 Configuration

用户设置保存在：

```text
%LOCALAPPDATA%\SLMapsOverlay\overlay.json
```

其中包括窗口位置、区域缩放与偏移、地图透明度、房间颜色、连接颜色、快捷键和地图来源。删除该文件会恢复默认设置。

User settings are stored at:

```text
%LOCALAPPDATA%\SLMapsOverlay\overlay.json
```

The file contains window placement, region scale and offsets, map opacity, room and connection colors, hotkeys, and map source. Delete it to restore the defaults.

## 本地地图生成 Local Generation

本地生成器使用 `map-generation-data.json`。该数据包含 atlas 网格、允许的旋转、固定房间、连接方向、房间 prefab 限制和显示字段。

The local generator uses `map-generation-data.json`. It contains atlas grids, allowed rotations, fixed rooms, connection directions, prefab limits, and display metadata.

本地生成的流程为：按 seed 选择各区域 atlas，选择旋转与房间顺序，按 prefab 最小/最大数量和权重分配房间，然后对 EZ/HCZ 检查点进行对齐。房间连接来自固定 atlas 结构，具体房间身份由 seed 决定。

The local generation flow selects an atlas per zone from the seed, chooses rotations and cell order, assigns prefabs using minimum/maximum counts and weights, and aligns the EZ/HCZ checkpoints. Connections come from the fixed atlas structure while room identities vary with the seed.

本地数据与提取数据对应的游戏版本有关。游戏更新后，可能需要重新提取资源并重建 `map-generation-data.json`。

The local data is tied to the game version from which it was extracted. After a game update, the asset data may need to be regenerated.

## 研究与验证 Research and Verification

研究脚本位于 `research/`。重新提取资源需要从游戏目录读取 `SCPSL_Data/level2` 和 `sharedassets2.assets`，并使用 UnityPy：

The research scripts are in `research/`. Asset extraction reads `SCPSL_Data/level2` and `sharedassets2.assets` from the game directory and requires UnityPy:

```powershell
python research/extract_map_assets.py
powershell -File research/collect_samples.ps1 -MaxSeed 50
python research/build_generation_data.py
python research/build_display_data.py
python research/verify_generation.py
dotnet run --project research/MapGeneratorVerification.csproj
```

验证程序会比较多个 seed 的房间数量、atlasIndex、坐标、顺序、形状、旋转、显示字段和连接关系，并检查备用 API 样本的 prefab 变体。

The verification program compares room counts, atlas indices, coordinates, order, shapes, rotations, display fields, and connections across multiple seeds. It also checks prefab variants from backup API samples.

## 故障排查 Troubleshooting

- 没有地图：确认游戏已经进入一局，并检查 `Player.log` 是否存在新的 seed 记录。
- 种子没有更新：确认游戏窗口处于前台，按 `PageDown`，或等待日志轮询刷新。
- 本地生成失败：确认 `map-generation-data.json` 位于程序目录，并使用与项目目标框架匹配的 .NET SDK。
- 名称为空：程序会隐藏没有中文翻译的英文标识；可在 `room.txt` 中补充翻译后重启程序。
- 地图位置不合适：打开设置面板分别调整核心区域和 LCZ 的缩放、X 偏移、Y 偏移。

- No map: make sure a round has started and that `Player.log` contains a new seed.
- Seed does not update: focus the game window, press `PageDown`, or wait for log polling.
- Local generation fails: make sure `map-generation-data.json` is beside the executable and that the required .NET SDK is installed.
- Missing names: untranslated English identifiers are intentionally hidden; add a translation to `room.txt` and restart the program.
- Incorrect map position: open the settings panel and adjust the core and LCZ scale and offsets separately.

## 许可证与数据来源 License and Data Sources

地图结构研究参考了公开的逆向分析文章和地图 API：

The map structure research references the following public reverse-engineering article and map API:

- [地图生成算法逆向分析 / Map generation reverse engineering](https://fxdyj.com/archives/scpsl-mapgen-reverse-engineering)
- [SCP:SL Maps API](https://scpslmaps.fxdyj.com/)

游戏资源只用于本地只读分析和验证；本仓库不包含游戏程序集或原始 Unity 资源。

Game assets are used only for local read-only analysis and verification. This repository does not include the game assembly or original Unity assets.
