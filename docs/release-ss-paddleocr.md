# 发布说明：SS.PaddleOCR 1.0.0 全家桶

2026-11-27。本仓库的所有 NuGet 包从 `Sdcb.SimdPaddleOCR*` 整体改名为 `SS.PaddleOCR*`，
并去掉版本后缀 `V5`；同时新增韩语 PP-OCRv5 mobile 包与中文 PP-OCRv6 small 包。
核心 id 是新 id（首版 1.0.0），与线上已有的 `Sdcb.SimdPaddleOCR` 2.0.0 互不影响。

## 要推的包（`nupkgs/`）

| 包                                         | 版本  | 大小     | 说明                                        |
| ------------------------------------------ | ----- | -------- | ------------------------------------------- |
| `SS.PaddleOCR.ModelProvider`               | 1.0.0 | 26 KB    | 先推；线上已有同名包则自动跳过               |
| `SS.PaddleOCR.Models.TextLineOrientation`  | 1.0.0 | 954 KB   | CLS 模型，被各模型包传递引用                 |
| `SS.PaddleOCR.Models.ChineseV6Tiny`        | 1.0.0 | 5.7 MB   | 中文 PP-OCRv6 tiny（DET+REC+字典）           |
| `SS.PaddleOCR.Models.ChineseV6Small`       | 1.0.0 | 26.1 MB  | 中文 PP-OCRv6 small（DET+REC+字典）          |
| `SS.PaddleOCR.Models.ChineseV6Medium`      | 1.0.0 | 95.7 MB  | 中文 PP-OCRv6 medium（DET+REC+字典）         |
| `SS.PaddleOCR.Models.KoreanV5Mobile`       | 1.0.0 | 12.9 MB  | 韩语 PP-OCRv5 mobile（DET+REC+字典）         |
| `SS.PaddleOCR`                             | 1.0.0 | 565 KB   | 核心，含 Concat 布局修复                     |

`.snupkg` 与同名 `.nupkg` 放在一起，`dotnet nuget push` 会一并推送符号包。
`models/medium_det.onnx`（62 MB）与 `models/medium_rec.onnx`（76.5 MB）原本不在仓库里，
本次从已发布的 `Sdcb.SimdPaddleOCR.Models.ChineseV6Medium` 包内嵌资源中提取补齐（字节数与
GitHub 上一致），因此 medium 包也能直接打。

## 打包

一条命令产出上表全部 7 个包（Windows PowerShell 5.1 与 PowerShell 7 都能跑）：

```powershell
powershell -ExecutionPolicy Bypass -File build/pack-nuget.ps1 -Clean
```

参数：

| 参数            | 作用                                                              |
| --------------- | ----------------------------------------------------------------- |
| `-Clean`        | 先清空 `nupkgs/` 里的旧 `.nupkg` / `.snupkg`                       |
| `-Version 1.0.1`| 一次性把所有包（含互相之间的依赖版本）改成同一版本                |
| `-Only <名字>`  | 只打指定包，可重复传多个；依赖包需已在 `nupkgs/` 里               |
| `-Configuration`| 默认 `Release`                                                    |
| `-OutDir`       | 默认 `nupkgs/`                                                    |

实现要点：脚本内置与 `Directory.Build.props` 一致的 7 包版本表，把
`-p:SSPaddleOCR…Version=x` 一次性传给每一次 `dotnet pack`；**不要**改成全局
`-p:Version=`，否则该版本会被套到所有 ProjectReference 上，依赖区间写错。
LINQPad 版 `build/00-common.linq` + `01-build-packages.linq` 仍然可用，版本表需与
`pack-nuget.ps1` 保持一致。

## 推送

需要一个对 `SS.PaddleOCR.*` 有 push 权限的 nuget.org API key（Account → API keys，
scope 选 "Push new packages and package versions"，glob 填 `SS.PaddleOCR.*`）：

```powershell
$env:NUGET_API_KEY = 'oy2...'
powershell -ExecutionPolicy Bypass -File build/publish-nuget.ps1
```

等价的手工命令（顺序无关）：

```powershell
$key = $env:NUGET_API_KEY
$src = 'https://api.nuget.org/v3/index.json'
dotnet nuget push nupkgs/SS.PaddleOCR.ModelProvider.1.0.0.nupkg                --api-key $key --source $src --skip-duplicate
dotnet nuget push nupkgs/SS.PaddleOCR.Models.TextLineOrientation.1.0.0.nupkg   --api-key $key --source $src --skip-duplicate
dotnet nuget push nupkgs/SS.PaddleOCR.Models.ChineseV6Tiny.1.0.0.nupkg         --api-key $key --source $src --skip-duplicate
dotnet nuget push nupkgs/SS.PaddleOCR.Models.ChineseV6Small.1.0.0.nupkg        --api-key $key --source $src --skip-duplicate
dotnet nuget push nupkgs/SS.PaddleOCR.Models.ChineseV6Medium.1.0.0.nupkg       --api-key $key --source $src --skip-duplicate
dotnet nuget push nupkgs/SS.PaddleOCR.Models.KoreanV5Mobile.1.0.0.nupkg        --api-key $key --source $src --skip-duplicate
dotnet nuget push nupkgs/SS.PaddleOCR.1.0.0.nupkg                              --api-key $key --source $src --skip-duplicate
```

`--skip-duplicate` 已在脚本里，重复跑不会报错。推完等几分钟索引，再看
<https://www.nuget.org/packages/SS.PaddleOCR>。

## 消费方式

```powershell
dotnet add package SS.PaddleOCR
dotnet add package SS.PaddleOCR.Models.KoreanV5Mobile     # 或 Models.ChineseV6Small / ChineseV6Tiny
```

```csharp
using SS.PaddleOCR;
using SS.PaddleOCR.Models.KoreanV5Mobile;

using PaddleOcrAll ocr = await PaddleOcrAll.LoadAsync(KoreanV5MobileModels.Default);
PaddleOcrResult result = ocr.Run(bgr24, width, height);
Console.WriteLine(result.Text);
```

包 id、程序集名、命名空间三者现在都是 `SS.PaddleOCR*` / `SS.PaddleOCR`，
`using SS.PaddleOCR;` 即可，无需再写 `Sdcb.` 前缀。

## 注意事项

- 旧包 `Sdcb.SimdPaddleOCR` 2.0.0 与新的 `SS.PaddleOCR` 1.0.0 类型/命名空间都不同，
  同一个项目里同时引用不会冲突；但 2.0.0 缺 Concat 布局修复，跑 PP-OCRv5 识别会出乱码。
- 两个模型包（韩语、中文 small）不声明对核心包的依赖（只依赖 `ModelProvider` 与
  `TextLineOrientation`），调用方必须自己加上 `SS.PaddleOCR`。
- 模型资产来源与许可：`korean_v5_*.onnx` 来自 official paddlex `paddle3.0.0` 推理模型
  （PaddleOCR Apache-2.0），韩语字典抽自 `inference.yml` 的 `character_dict`（11945 行）；
  `small_det.onnx` / `small_rec.onnx` 是 PP-OCRv6 small，字典为 `models/rec_keys.txt`
  （18708 行，与已发布 `Sdcb.SimdPaddleOCR.Models.ChineseV6Small` 包内字典逐行一致）。
- `models/` 里的 v6 二进制与 `assets/icon.png` 是仓库原本就该有的文件，本次补齐后才打得动模型包。

## 验证记录（本地已完成）

- `dotnet test test/SS.PaddleOCR.UnitTests` → 88 通过 / 1 跳过 / 0 失败，其中包含
  `KoreanV5MobilePackageTests` 5 个与 `ChineseV6SmallPackageTests` 5 个断言。
- 干净目录只装 nupkg（`SS.PaddleOCR` + `Models.KoreanV5Mobile` + `Models.ChineseV6Small`）：
  - 韩语整页：`안녕하세요` 1.0000、`대한민국 서울` 0.9696、`오늘 날씨가 좋네요` 0.9945，261 ms 三行。
  - 中文整页（v6 small）：`你好世界` 1.0000、`中文识别测试` 0.9992，234 ms 两行。
  - 中文整页（v6 medium）：`你好世界` 1.0000、`中文识别测试` 0.9999，397 ms 两行。
- 与 ONNX Runtime 逐元素对拍：rec 4/4、det 3/3、cls 1e-6 内一致；修复前后 A/B 见
  [docs/v5_rec.md](v5_rec.md) 的「补记 2026-11-26」。