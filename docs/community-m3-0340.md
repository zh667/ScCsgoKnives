# 0.34.0 复查修正：旧存档枪械自动识别、第三人称改走骨骼计算钩子、耐久计数与音效

日期：2026-09-07。针对 0.33.0 实机复查（`World29` 旧世界里 AWP 被读成"加利尔 57%"、第三人称没变化、Sand/Leaves 音效缺失、
耐久计数串枪、两个测试仍用旧编码）逐条处理。

## 1. 旧世界的枪不再被误读

0.32 的 v4 布局把 3 位耐久档放在第 14–16 位，而 0.20.5–0.31.0 的 v3 布局在第 16 位固定为 1、第 15 位固定为 0，
所以 v3 物品落在耐久码 `100`/`101` 上，被当成"57%/71% 的另一把枪"。

现在耐久改为 **6 档**（5 = 全新，0 = 损坏；100/80/60/40/20/0 %），六档存成 `000 001 010 011 110 111`，
**`100` 和 `101` 永远不由 v4 写出**——读到它们就按 v3 解码（型号、弹量、消音器照旧，耐久按满），
下一次写入（开枪、换弹）自动重打包成 v4。旧箱子里的 AWP（data 65858）现在读出来就是 AWP、5 发、100%。
自检把 35 把枪 × 4 种弹量 × 消音器的 v3 值全部往返一遍，并断言任何 v4 值都不会被认成 v3。

代价：0.32/0.33 里**已经被开过枪或换过弹**的旧枪已经按误读型号写回（你背包里那两把"加利尔"），
它们现在是真正的 v4 加利尔，无法还原；没碰过的（箱子里的）会正确显示。v1/v2（0.20.4 及更早）仍不解码。
六档比七档粗一点，但换来旧档零配置兼容；每档击发数相应变为 AK 300、AWP 40、电击枪 20。

## 2. 第三人称为什么没变

SCAPI 1.9.2.1 的人形模型走 `AnimationController`（`Animations/Human.json`），`ComponentCreatureModel.Animate`
在这条路径上**不会**调用 `OnModelAnimate` 钩子，所以 0.33.0 的姿态代码从未运行，画的仍是原版小方块。
改为 `OnModelCalculateBones` 钩子：它在动画控制器算完、绝对矩阵合成前每个摄像机调一次，我在这里每帧解一次姿态并改写两只手骨。
其余逻辑不变。实机再看一次；`Game.log` 里应出现 `third person awp: … grips … right arm …`。

## 3. 耐久计数串枪

档内计数的键从"玩家×格"改为"玩家×格×型号×消音器×档位"：维修后档位变化即重新计数；同型号同档位的另一把枪仍会继承（没有实例 ID 分不开，规划已知）。存档格式相应变为 `型号,消音器,档位,发数`。

## 4. 命中音效

原版只有 `Audio/Impacts/{Body,Dirt,Glass,Metal,Plant,Soft,Stone,Wood}`，方块材质名里还有 Leaves/Sand/Snow。
现在映射：Leaves→Plant，Sand→Dirt，Snow→Soft，空材质不放，未知材质→Stone。

## 5. 测试

`InteractionRegression`、`SwitchAnimationRegression` 改为通过 `GunSpec.MakeData` 造枪，不再写死 `65536+型号`。

## 6. 验证

VPS 无头：PackageCheck（含 Content.zip）6145/6145，运行时自检 5874/5874（`community-m3-0340-packagecheck.json`）。本版起 VPS 不再打包，由 Windows 侧编译打包。

## 7. 不属于本模组

开箱模组奖励表里的 `GunpowderKegBlock` 不存在（原版是 Small/Medium/LargeGunpowderKegBlock），那是 ScCsgoBox 的条目，这里没改。
