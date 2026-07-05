# Overlayer × ADOFAI Unity 6 兼容修复 / Unity 6 Compatibility Fix

让 **Overlayer 3.49.0** 在 **《冰与火之舞》Unity 6 新版本(r145+)** 上正常运行。

Makes **Overlayer 3.49.0** work on the **Unity 6 builds of A Dance of Fire and Ice (r145+)**, where the stock mod crashes on load.

> Overlayer 原作者为 [c3nb](https://github.com/c3nb/Overlayer),3.49.0 由 [modlist.org](https://github.com/modlist-org/Overlayer) 维护发行。本仓库不是 Overlayer 的源码分支——3.49.0 以二进制发行,这里提供的是**二进制补丁器 + 兼容层**,只做兼容修复,不改动任何功能。
>
> Overlayer was originally made by [c3nb](https://github.com/c3nb/Overlayer); 3.49.0 is maintained and distributed by [modlist.org](https://github.com/modlist-org/Overlayer). This repo is **not** a source fork — 3.49.0 ships as a binary, so this is a **binary patcher + compat shim**. Compatibility only, no functional changes.

## 📥 安装 / Install

1. 从 [Releases](../../releases) 下载 `Overlayer_v3.49.0_unity6fix.zip`
2. 解压到游戏 `Mods/` 目录(覆盖原有 `Overlayer/` 文件夹)
3. 用 UnityModManager 正常启动即可

Download the zip from [Releases](../../releases), extract into the game's `Mods/` folder (replacing the existing `Overlayer/`), and launch via UnityModManager.

已在 Windows 上实测(2026-07),伴生 mod OverlayerAttempts(`{Attempts}` tag)无需修改即可继续使用。

Tested on Windows (July 2026). The companion mod OverlayerAttempts (`{Attempts}` tag) keeps working unmodified.

## 🔧 修了什么 / What broke in Unity 6

游戏升级 Unity 6 后经历了多星球重构等大量 API 变动,Overlayer 3.49.0 共有 **70 处**失效引用,全部由补丁器改写为调用 `Overlayer.Unity6Compat.dll` 兼容层(或直接重定向):

The Unity 6 update (multi-planet refactor etc.) broke **70** references in Overlayer 3.49.0. The patcher rewrites all of them to the `Overlayer.Unity6Compat.dll` shim (or retargets them directly):

| 变动 / Change | 修复 / Fix |
|---|---|
| `scrController` 的 `speed`/`isCW`/`failbar`/`midspinInfiniteMargin`/`consecMultipressCounter` 等字段移到 `PlanetarySystem`/`scrPlayer` | 兼容层转发 / forwarded by shim |
| `scrMistakesManager` 的 `hitMargins`/`hitMarginsCount`/`GetHits` 移到 `scrMarginTracker` | 兼容层转发 / forwarded by shim |
| `RDString.Get` 三参重载与 `AvailableLanguages` 变动 | 兼容层转发 / forwarded by shim |
| `ADOBase.isPlayingLevel` 被移除 | 按 r141 语义重实现 / reimplemented with r141 semantics |
| `SFB.StandaloneFileBrowser`(Assembly-CSharp-firstpass)被移除 | 基于新 `UnityFileDialog.dll` 重实现 / reimplemented on `UnityFileDialog.dll` |
| 判定文字 `scrHitTextMesh.text`:`TextMesh` → `TextMeshPro`,缓存字典移到 `scrHitTextManager`(转 nonpublic) | `SetJudgeText` lambda IL 手术 + FieldRef 替身 / IL surgery + FieldRef stand-ins |
| `OnDamage` 补丁目标 `scrController` → `scrPlayer`;`CalculatePercentAcc` → `CalculateTotalAccuracy` | 改写 `LazyPatch` attribute 参数与 Postfix 参数类型 / attribute + parameter retype |
| `GCNS` 类型没了、`scrController.startRadius` 没了 | `ldtoken` 重定向到 `Releases`;`InjectStartRadius` 置空 / retarget & no-op |
| `PlanetRenderer.ring`:`SpriteRenderer` → `LineRenderer`;`TMP_Asset.material` 字段→属性 | 兼容层转发 / forwarded by shim |

补丁后验证:全模块 2721 处引用解析通过;2377 个方法体 IL 往返无损。

Verified after patching: all 2721 references in the module resolve; IL round-trips losslessly across 2377 method bodies.

## 🛠️ 自己打补丁 / Patching it yourself

在 Linux 上(mono + mcs + dotnet SDK):

```bash
# 1. 把原版 Overlayer 3.49.0 的 Overlayer.dll 放到 orig/
# 2. 从 NuGet Mono.Cecil 包取 lib/net40/Mono.Cecil.dll 放到 Patcher/
# 3. 指定游戏 Managed 目录,一键跑完:编译兼容层 -> 编译补丁器 -> 打补丁
GAME_MANAGED="/path/to/A Dance of Fire and Ice_Data/Managed" ./patch.sh
```

产物 / outputs:

- `Overlayer.patched.dll` → 装到 `Mods/Overlayer/Overlayer.dll`
- `Compat/bin/Overlayer.Unity6Compat.dll` → 装到 `Mods/Overlayer/lib/`

补丁器若发现改写数异常(< 60)会中止,避免打在不匹配的版本上。

The patcher aborts if it applies suspiciously few rewrites (< 60), so it won't silently mangle a mismatched Overlayer build.

## 📁 仓库内容 / Layout

- `Patcher/OverlayerPatcher.cs` — Mono.Cecil 补丁器,原地改写 IL(不破坏分支目标)/ in-place IL rewriter
- `Compat/Compat.cs` — 兼容层源码(含 SFB 重实现)/ compat shim source (incl. SFB reimplementation)
- `Compat/build.sh` — 兼容层构建脚本 / shim build script
- `patch.sh` — 一键完整管线 / one-shot pipeline

## ⚖️ 许可 / License

本仓库的补丁器与兼容层代码采用 [MIT License](LICENSE)。Overlayer 本体及其发行文件版权归原作者所有,Release 中的补丁版仅为兼容修复再分发,如原作者提出异议将立即撤下。

The patcher and shim code in this repo are [MIT licensed](LICENSE). Overlayer itself remains the property of its authors; the patched build in Releases is redistributed solely as a compatibility fix and will be taken down at the original authors' request.
