# 0.31.0 社区反馈 M2（逻辑部分）：火提前起烟、高爆暂时炸开烟、统一密度查询

日期：2026-09-07。对应 `docs/community-feedback-plan-2026-09-07.md` 的 F05、F06 和 F07 的"统一密度/可见性查询"基础。
F07 的画面遮挡（烟外看不穿、烟内按浓度过渡、透明 pass 顺序）需要在真机上看深度/合成能力才能定方案，这版没动渲染路径。

## F05 火触发烟

- 已投出、尚未起烟的烟雾弹进入**有效**燃烧区（燃烧瓶/燃烧弹的火区，与生物受火用同一个圆柱判定 `ScFireArea.Contains`），
  且火区原点到弹体之间没有方块阻挡，就在当前位置立即起烟（`ScFireArea.Heats` + `Clear`）。
- 一次更新内的顺序固定：火区有效性（落水/被烟熄灭）→ 弹体运动 → 受热触发 → 引信/落稳/烟成长；每枚烟只触发一次（起烟后 `Effect=true` 不再判定）。
- 背包里的烟、诱饵、已熄灭的火区、隔墙都不触发。烟起后照旧在下一次更新把碰到的火灭掉，音效只有起烟一声。
- 日志：`grenade smoke heated by fire kind 3 at …: pops early at … after 0.83 s`。

## F06 高爆炸烟

- 高爆起爆时（只有高爆，闪光/诱饵不算），对每团能直线看到、且距离在 3 m + 烟半径内的活动烟，生成一个**扰动**
  `ScSmokeDisturbance`：半径 3 m，完全打开 1.5 s，再用 2 s 线性回填（三个数都是估计）。隔墙/隔层的烟不受影响。
- 扰动不删烟、不重置烟寿命；多个高爆取最大清除度；随存档保存/读取（`SmokeDisturbances`）；最多 16 个。
- 同一个清除值同时作用于：烟粒子 alpha（可见开口）、烟内遮罩、AI 视线（`ScSmokeVolume.Blocks` 用扣除扰动后的有效烟内长度）。
- 日志：`HE at … opens 1 smoke(s): radius 3 m, hold 1.5 s, recovery 2 s`。

## F07 基础：统一密度查询

`ScSmokeVolume.Density(smoke, point, disturbances)`：球内 1、外 0、边缘 0.5 m 过渡，再乘 (1 − 清除度)；
`EffectiveInsideLength` 沿视线每 0.25 m 采样积分。AI 的三处视线判定、追击评分、烟内遮罩、烟粒子现在读同一套值。
画面遮挡本身（F07 主体）待实机：需要先看烟外看穿/烟内看穿/透明实体覆盖/名字血条泄露分别是什么样。

## 验证（VPS 无头）

- 新增自检 `smoke-heated-by-fire`（圆柱内外、上方、已灭火、已起烟不重复、诱饵不算）、`smoke-he-opening`（保持期/回填期/边缘/存读档校验）、
  `smoke-opening-opens-sight`（原本挡视线的烟被打开后不挡、旁边的开口不影响、回填后重新挡）。
- PackageCheck（含 Content.zip）6129/6129，运行时自检 5858/5858；结果见 `community-m2-0310-packagecheck.json`。
- 包：`output/ScCsgoKnives-0.31.0-Lite.scmod`，111.8 MB，SHA-256 `c0bddadf56108360ceb6e026052db32f7d9ae73f1f3e407946e267d7d812d920`。

## 实机要看

烟投进火里立刻起烟；火投到等待中的烟旁边（3 m 内）起烟；隔墙不起；烟起后火灭。
高爆扔进烟里：中间开一个洞约 1.5 s 后逐渐合上，洞里能看见并被动物看见；烟边缘炸只开一角；隔墙炸不开。
