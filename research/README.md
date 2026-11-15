# SCP:SL 地图生成复现

运行时使用 [map-generation-data.json](../map-generation-data.json) 中的 17 张 Beta atlas 和 72 个房间 prefab 数据。atlas 决定房间格子的结构，seed 决定三区的 atlas 索引、格子旋转、格子顺序及实际 prefab。三区共用一个 .NET 旧版 `System.Random` 状态，依次生成 LCZ、HCZ、EZ；EZ 通过两处 HCZ 检查点对齐。每个区域按 prefab 的最少/最多数量、权重与邻接权重分配房间。

当前数据来自 SCP: Secret Laboratory 15.0 Beta（Steam build `26655685`，Unity `6000.4.11f1`）。EZ 的 `EZ_GateA` 和 `EZ_GateB` prefab 各有一个主格连接点，并包含额外的内部连接器；这些内部连接器不是 Atlas 相邻房间，因此只作为 `connectorPorts` 元数据保存。EZ 新 Atlas 有 12 个静态分析中无法与同层格互相确认的端口，生成器会保留原始记录并省略无法确认的地图线，等待新版同种子地图导出进行校准。

数据取自本机游戏 `SCPSL_Data/level2` 和 `sharedassets2.assets`。坐标映射及显示元数据参考 [逆向文章](https://fxdyj.com/archives/scpsl-mapgen-reverse-engineering) 和[公开地图 API](https://scpslmaps.fxdyj.com/)。游戏资源读取是只读操作；研究中下载的 API 样本保存在忽略目录 `.tools/samples`。

重建数据需要 Python 和 UnityPy：

```powershell
python research/extract_map_assets.py
python research/build_generation_data.py
python research/verify_generation.py
dotnet run --project research/MapGeneratorVerification.csproj --no-restore
```

`verify_generation.py` 校验 Beta 资源指纹、705 个格子、17 张 atlas、72 个 prefab、连接互反性和 Gate A/B 连接器元数据。C# 验证程序另外用 `.tools/legacy-baseline/` 对 50 个历史样本和两份备用样本进行回归，并对 Beta 数据运行 100 个 seed 的确定性检查。历史 API 样本不是 15.0 Beta 的实测地图证据；详见 [15.0-beta.md](15.0-beta.md)。
