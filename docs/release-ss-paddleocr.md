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

## 用 GitHub Actions 发布（不用本地 key）

工作流：[`.github/workflows/publish-nuget.yml`](../.github/workflows/publish-nuget.yml)，
在 `windows-2025` 上依次执行 `build/pack-nuget.ps1 -Clean` 和 `build/publish-nuget.ps1`，
7 个 `.nupkg` 也作为构建产物上传保留 14 天。

一次性配置：

1. nuget.org → Account → API keys → Create：scope 选 "Push new packages and package versions"，
   glob pattern 填 `SS.PaddleOCR*`；
2. 仓库 → Settings → Secrets and variables → Actions → New repository secret，
   名字 **`NUGET_API_KEY`**，值粘贴上面那把 key；
3. Actions → `publish-nuget` → Run workflow：
   - 先跑一次 `dry_run = true`：只打包 + 打印推送计划，不需要 key，不会发布任何东西；
   - 再跑 `dry_run = false`：真正推送（`--skip-duplicate`，重复跑安全）。
   - `version` 留空 = 用 `Directory.Build.props` / `pack-nuget.ps1` 里的版本；填 `1.0.1` 则整批改版本。

key 只从 secret 读进进程环境再交给 `dotnet nuget push --api-key`，不落盘、不写日志。
不想长期保存 key 的话，可以改用 nuget.org 的 **Trusted Publishing**（OIDC）：删掉 secret，
给工作流加 `id-token: write`，把发布步骤换成 `NuGet/login@v1` + 它输出的短时 key，
详见 <https://learn.microsoft.com/nuget/nuget-org/trusted-publishing>。

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

## 已发布到 nuget.org（2026-11-27）

- 工作流 run [`45936281434`](https://github.com/516018579/SS.PaddleOCR/actions/runs/45936281434)：
  `dry_run = false`，2026-11-27T03:48:20Z → 03:49:55Z，7 个 `.nupkg` 与 7 个 `.snupkg`
  逐个 `Your package was pushed`，无告警级失败（仅上游 `Intrinsics.cs` 的 CS0108 编译警告）。
- 同一工作流的前两次 run（`45933914425`、`45934923697`）是 `dry_run = true`，
  日志停在 `WhatIf: nothing was pushed`，只产出了 nupkgs artifact。
- 发布结果（azuresearch `packageid:` 均命中，registration `listed = true`）：

  | 包 | 版本 | published (UTC) |
  | --- | --- | --- |
  | SS.PaddleOCR | 1.0.0 | 2026-11-27T03:49:44Z |
  | SS.PaddleOCR.ModelProvider | 1.0.0 | 2026-11-27T03:49:27Z |
  | SS.PaddleOCR.Models.TextLineOrientation | 1.0.0 | 2026-11-27T03:49:29Z |
  | SS.PaddleOCR.Models.ChineseV6Tiny | 1.0.0 | 2026-11-27T03:49:32Z |
  | SS.PaddleOCR.Models.ChineseV6Small | 1.0.0 | 2026-11-27T03:49:35Z |
  | SS.PaddleOCR.Models.ChineseV6Medium | 1.0.0 | 2026-11-27T03:49:39Z |
  | SS.PaddleOCR.Models.KoreanV5Mobile | 1.0.0 | 2026-11-27T03:49:42Z |

- 依赖关系与 nupkg 内 README 已在 nuget.org 上核对：核心包依赖 `ModelProvider`，
  各模型包依赖 `TextLineOrientation` + `ModelProvider`，与本地 nuspec 一致。
- 元数据遗留（1.0.0 不可变，只能发新版本才能改）：`Directory.Build.props` 里的
  `<Authors>sdcb</Authors>`、`PackageProjectUrl`、`RepositoryUrl` 仍是上游
  `https://github.com/sdcb/SimdPaddleOCR`，所以 1.0.0 的 Project Site / Repository 链接
  指向上游仓库，而不是本仓库。该遗留已由 1.0.2 修正，见下节。

## 后续版本（2026-11-27 / 28）

先只发核心包 `SS.PaddleOCR`，6 个模型包当时内容未变、停在 1.0.0（工作流对已存在的版本
`--skip-duplicate`，每次都会重推一遍但被 nuget.org 以 `already exists` 跳过）；随后把
6 个模型包也升到 1.0.1，让它们带上同样的元数据修正，见本节末尾。

| 版本 | run | 内容 |
| --- | --- | --- |
| 1.0.1 | [`46025109109`](https://github.com/516018579/SS.PaddleOCR/actions/runs/46025109109) | 行方向分类器的双判据兜底：分类器投 180° 时先按正立识别，只有翻转后识别分数更高才真转（`VerifyFlipsByRecognition` 默认开、`FlipVerifyMinClassifierScore` 默认 0）。韩语漫画样张从 4/5 行翻错变为 5/5 正确。 |
| 1.0.2 | [`46031307150`](https://github.com/516018579/SS.PaddleOCR/actions/runs/46031307150) | 元数据修正：`Authors` = `Rinne`、`PackageProjectUrl` / `RepositoryUrl` = `https://github.com/516018579/SS.PaddleOCR`。内容与 1.0.1 相同。 |
| 模型包 1.0.1 | [`46033338717`](https://github.com/516018579/SS.PaddleOCR/actions/runs/46033338717) | `ModelProvider`、`TextLineOrientation` 与 4 个模型包从 1.0.0 升到 1.0.1，只带元数据修正，内容不变；核心包仍是 1.0.2（重推被 `already exists` 跳过）。 |

- 两次都是先 `dry_run = true` 验证日志打出对应的 `SS.PaddleOCR.<ver>.nupkg` 与
  `WhatIf: nothing was pushed`，再 `dry_run = false` 真推；`--skip-duplicate` 让 6 个
  已存在的模型包变成 `already exists`，只有核心包的 `.nupkg` / `.snupkg` 各出现一次
  `Your package was pushed`。
- 版本号的真相在 `build/pack-nuget.ps1` 的版本表（它用 `-p:<Name>Version=` 覆盖
  `Directory.Build.props`），改版本时必须三处同步：`Directory.Build.props:7`、
  `build/00-common.linq:31`、`build/pack-nuget.ps1:47`。
- nuget.org 侧核对（2026-11-28T00:18Z）：`azuresearch` 的 `packageid:` 最新版本为
  1.0.2；1.0.2 包页 Authors 显示 `Rinne`，Project Site 指向
  `https://github.com/516018579/SS.PaddleOCR`，Repository 指向同名 `.git`。
  README 正文里的「源仓库 https://github.com/sdcb/SimdPaddleOCR」是对上游的出处说明，
  保持原样。
- 模型包 1.0.1 的版本表同样三处同步（`Directory.Build.props:8-13`、`build/00-common.linq:32-37`、
  `build/pack-nuget.ps1:41-46`），模型包依赖也随之指向 `TextLineOrientation 1.0.1` +
  `ModelProvider 1.0.1`（本地解包 nuspec 核对）。
- 最终核对（2026-11-28T00:41Z，`azuresearch` 逐包 `packageid:` 轮询约 7 分钟才全部刷新）：
  7 个包全部命中 `authors = Rinne`、`projectUrl = https://github.com/516018579/SS.PaddleOCR`，
  版本为 核心 1.0.2 / 其余 1.0.1。

## 模型包 1.0.2（2026-11-28）：韩语字典把 YAML 引号当成了字符

`models/korean_v5_keys.txt` 是从 official paddlex `inference.yml` 的 `character_dict` 抽出来的。
YAML 会给需要保护的标量加单引号（`'!'`、`'"'`、`'#'`、`','`、`'0'`…，共 31 条，正是 YAML 保留字符
和「看起来像数字」的那些），抽取脚本把引号当成了字符内容一起保留下来。字典本身行数/顺序没错
（11945 行、`ClassCount` 11947），所以模型输出对、解码文本错：本该是 `침해,청소년보호법` 的位置
被拼成 `침해','청소년보호법`——逗号两侧多出两个单引号，`!`、`?`、`~`、数字同理。

修复：按 YAML 规则去掉这 31 条的外层引号（含 `''''` → `'`）。字典从 47514 字节变为 47451 字节，
行数不变。回归钉在 `test/SS.PaddleOCR.UnitTests/KoreanV5MobilePackageTests.cs` 的
`DictionaryHasExpectedShape`：钉住索引 11504 `!`、11510 `'`、11515 `,`，并断言没有条目首尾带引号。

本地验证（`ko_comma.bmp` / `ko_bubble.bmp` 为本地样张，未入库）：

| 样张 | 修前 | 修后 |
| --- | --- | --- |
| `ko_comma.bmp`（1211x336，4 行） | `저작권 침해','청소년보호법 위반','재산권 침해 등의` | `저작권 침해,청소년보호법 위반,재산권 침해 등의`（0.9844） |
| `ko_bubble.bmp`（5 行漫画字） | `어머'~''!'`、`얘들아'!'`、`이렇게나 컸다고'?'` | `어머~!`、`얘들아!`、`이렇게나 컸다고?` |

`tools/PackageSmokeTest` 原先把三个模型包固定成 NuGet 引用，所以本地改字典跑不出来；本轮加了
`-p:UseProjectModels=true`（与 `UseProjectCore=true` 并列），把模型包也换成工程引用，才能用冒烟工具
验证嵌进包里的字典。

版本：`SS.PaddleOCR.Models.KoreanV5Mobile` 1.0.1 → **1.0.2**（三处同步：`Directory.Build.props:13`、
`build/00-common.linq:37`、`build/pack-nuget.ps1:46`）；其余包不动。核心包内容未变，仍是 1.0.2。
已发布的 1.0.0 / 1.0.1 模型包含带引号的字典，只有装了新版模型包的调用方才会拿到修好的解码文本。