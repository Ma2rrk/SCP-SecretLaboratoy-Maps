# SCP:SL 地图生成复现

运行时使用 [map-generation-data.json](../map-generation-data.json) 中的 20 张 atlas 和 61 个房间 prefab 数据。atlas 决定房间格子的结构，seed 决定三区的 atlas 索引、格子旋转、格子顺序及实际 prefab。三区共用一个 .NET 旧版 `System.Random` 状态，依次生成 LCZ、HCZ、EZ；EZ 通过两处 HCZ 检查点对齐。每个区域按 prefab 的最少/最多数量、权重与邻接权重分配房间。

数据取自本机游戏 `SCPSL_Data/level2` 和 `sharedassets2.assets`。坐标映射及显示元数据参考 [逆向文章](https://fxdyj.com/archives/scpsl-mapgen-reverse-engineering) 和[公开地图 API](https://scpslmaps.fxdyj.com/)。游戏资源读取是只读操作；研究中下载的 API 样本保存在忽略目录 `.tools/samples`。

重建数据需要 Python 和 UnityPy：

```powershell
python research/extract_map_assets.py
powershell -File research/collect_samples.ps1 -MaxSeed 50
python research/build_generation_data.py
python research/build_display_data.py
python research/verify_generation.py
dotnet run --project research/MapGeneratorVerification.csproj
```

最后一项使用本地 50 个公开样本和两份 `backup-api-*.json`，核对 `atlasIndex`、房间位置、顺序、形状、旋转、名称、标签及连接，并按备用 API 核对 prefab 变体。LCZ 走廊编号会随 seed 变化，目前仅输出可稳定确定的编号；地图结构和连接不依赖该编号。
