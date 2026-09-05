# ScCsgoKnives

《生存战争 2》SCAPI 1.9.2.1 的 CS2 武器模组，与 `ScCsgoBox` 联动。当前版本：0.20.4。

- 22 把刀、35 把枪，全部使用 CS2 模型、骨骼动画和真实手臂／手套。
- 旧配置中的 `KnifeProfile=0`、`GunProfile=0` 不再启用方块手。
- 电击枪冷却 10 秒，切枪继续计时，随世界保存；旧存档过长的剩余冷却最多保留 10 秒。
- AUG、SG553 开镜保留真实镜筒，镜片透出世界，外围可见场景，中心绿色光点。外形按用户 CS2 录屏校准。
- 物品变体顺序保持兼容既有存档；普通检视继续使用“编辑物品”（PC 默认 G），枪械换弹使用 R。

构建和交付：

```powershell
dotnet build ScCsgoKnives.sln -c Release
python -X utf8 tools/pack_scmod.py
```

安装包：`output/ScCsgoKnives-0.20.4.scmod`。资源来源见 `ASSET_SOURCES.md`，第三方署名见 `THIRD_PARTY_NOTICES.md`。

0.20.4 的清理清单、验证结果和视觉限制见 `docs/cs2-0.20.4-report.md`。代码采用 GPL-3.0。
