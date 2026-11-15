# 仓库协作说明

## 沟通要求

- 除非用户要求使用其他语言，否则使用中文回复。
- 进度和完成报告保持简洁，概述行为变化和验证结果。
- 保留与当前任务无关的用户改动。编辑或暂存前先检查 `git status`。

## 项目结构

- 本项目是 SCP: Secret Laboratory 的 Windows WinForms/.NET 10 地图覆盖层。
- `MainForm.cs` 管理覆盖层界面、设置面板、地图绘制、输入和 API 选择；`MainForm.Designer.cs` 初始化 WinForms 控件和设计器预览入口。
- `SeedKeyBindings.cs` 解析按键映射与绑定命令，默认文本为内嵌的 `Resources/seed-bindings.txt`，兼容程序旁的可选 BAT 文本；`MapFontManager.cs` 管理地图字体、绘图字体缓存和资源释放。
- `LocalMapGenerator.cs` 按设置选择 `map-generation-data.json`（15.0 Beta）或 `map-generation-data-14.2.9.json`（14.2.9）生成地图；`UseBetaMapGeneration` 默认开启，切换后重载当前本地地图并自动保存。
- 当前运行时数据对应 SCP:SL 15.0 Beta；重建时先提取游戏目录资源，再运行 `build_generation_data.py`。`research/atlas-topology-findings.json` 记录尚未能从静态 Atlas 确认的 EZ 端口，不能删除或用放宽检查掩盖。
- `MapModels.cs` 解析地图响应并定义地图和房间模型。
- `SeedMonitor.cs` 查找游戏日志并读取地图种子。
- `room.txt` 保存房间名称翻译。
- `research/` 存放资源提取、数据生成和验证工具。

## 实现约定

- 改动范围应贴合用户提出的行为；除非任务明确要求，否则保留现有地图布局、房间标签和输入行为。
- 两份 `map-generation-data*.json` 都是运行时必需数据，通过项目文件复制到生成程序旁边。
- 新配置默认使用本地生成，已有配置明确选择的地图来源应保留；地图来源选项使用中文。
- 种子按键列表及游戏键码 `kc` 从映射文本的 `$KEYMAP` 读取，绑定命令从 `Sync-Cmdbinding` 读取。优先读取程序旁可选的 `scpsl-seed-detector.bat`，缺少时使用内嵌的 `Resources/seed-bindings.txt`；发布包不包含 BAT。切换按键立即生效并复制控制台命令，不要在代码中硬编码游戏键码、执行 BAT 或自动改写游戏绑定文件。
- 地图字体通过下拉框选择已安装字体，切换立即重绘，不提供导入按钮或切换提示。配置字段为 `MapFontFamily` / `MapFontFile`，保留已有字体文件配置的读取兼容；不可用时回退系统字体。`MapFontSize` 为 `0` 时沿用随地图缩放的自动字号，固定字号支持 `6–48 pt`。`MapFontBold` 默认开启，字体缓存须同时匹配字号和实际样式，不支持请求样式时回退可用样式。`UseClearType` 默认关闭，开启使用 `ClearTypeGridFit`，关闭使用 `AntiAliasGridFit`；所有字体选项立即生效并自动保存。
- 地图生成在后台执行；过期请求不得覆盖新地图或在窗口关闭后更新界面。连接和中文标签按地图缓存，字体按区域及字号缓存；切换地图、翻译数据或字体时须使对应缓存失效，退出时释放资源。
- Atlas 格子及其旋转决定固定连接关系。修改连接处理时，同步更新生成的连接数据和相关构建、验证脚本。
- EZ Gate A/B prefab 的内部连接器统计保存在模板 `connectorPorts` 字段；它们不是同层 Atlas 相邻房间，不能直接转换为地图格连线。
- 房间名称使用现有翻译流程。不要显示未翻译的英文房间名；MicroHID 显示为 `HID`。
- EZ/HCZ 与 LCZ 的缩放和拖动设置应相互独立。匹配房间连接时同时使用 X、Y、Z 坐标，避免连接不同楼层的房间。
- 滚轮消息只处理一次，使用消息携带的屏幕坐标判断对应地图区域；覆盖层收到面板区域的滚轮时滚动面板，不缩放地图。地图缩放值与对应滑块使用相同精度；程序同步控件时暂停 `ValueChanged` 回写，避免覆盖另一地图的缩放或拖动偏移。
- 设置界面控件应放在可滚动的设置面板内。检查控件边界和间距，避免滑块、标签、颜色控件和按钮重叠。
- 半透明面板绘制前先通过 `base.OnPaintBackground` 恢复父背景，再进行透明混合；滚动、移动和尺寸变化时重绘背景及子控件。移动时使用 `SWP_NOCOPYBITS` 禁止复用旧位置的混合像素，并立即重绘父窗口旧、新位置的联合区域；验证须覆盖连续移动后旧位置恢复，不能只检查面板原地重绘。
- 设置面板通过标题/副标题拖动，位置保存在 `ControlPanelX` / `ControlPanelY`；移动及窗口尺寸变化后限制在客户区内。拖动保存不关闭面板，也不得触发地图拖动或修改地图缩放、偏移；滑块初始化须保留千分位缩放及十分位偏移精度。
- 用户设置通过 `OverlaySettings` 保存到 `%LOCALAPPDATA%\\SLMapsOverlay\\overlay.json`。旧配置缺少新字段时，应沿用默认值。
- 不要写死特定用户的游戏日志路径，使用现有的自动路径发现逻辑。

## 构建与验证

- 使用以下命令构建覆盖层：

  ```powershell
  dotnet build SLMapsOverlay.csproj --no-restore -p:UseAppHost=false -p:OutputPath=obj/verify-build/
  ```

- 如果修改了 `research/` 或地图生成逻辑，运行：

  ```powershell
  dotnet run --project research/MapGeneratorVerification.csproj --no-restore
  python research/verify_generation.py
  ```

- 提交前运行 `git diff --check`。
- 发布 Windows x64 自包含压缩包：`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Release.ps1`。输出位于已忽略的 `artifacts/`，包含程序、两个版本数据、默认配置、翻译和说明；附带源提交标识及 SHA-256，不包含 BAT、游戏文件或研究中间产物。
- 覆盖层运行时可能锁定常规构建输出。验证时使用 `obj/` 下单独的输出目录；不要仅为构建而停止用户正在运行的程序。
- 构建成功不代表界面行为已验证。如果没有重启程序进行运行时检查，应明确说明。
- 修改热键或字体时，验证 BAT 键码解析、剪贴板命令、字体回退与热切换，以及小屏幕下设置面板滚动和控件边界；绘图缓存优化应对比连接与标签结果。

## 仓库清洁

- 遵循 `.gitignore`。不要暂存 `.tools/`、`.idea/`、`bin/`、`obj/`、Python 缓存、本地游戏程序集或资源、运行日志及下载的 API 样本。
- 除非应用运行所需或用户明确要求，否则不要添加研究脚本生成的中间产物。
- `research/` 下的脚本可能读取本地游戏资源，并将中间文件写入已忽略的 `.tools/` 目录。
- 除非用户明确要求，否则不要提交或推送改动。

## 维护本说明

- 项目架构、工作流程、构建或验证命令、仓库清洁规则发生实质变化时，同步更新本文件。
- 保持说明简洁且可执行；清理过时内容，避免积累相互矛盾的规则。
