# ScCsgoKnives

《生存战争 2》SCAPI 1.9.2.1 的 CS 风格近战刀具模组，作为独立内容模组与 `ScCsgoBox` 联动。

当前原型包含：

- 爪子刀
- M9 刺刀
- 蝴蝶刀
- 三把刀的手持模型、基础色贴图和 `deploy/inspect/idle` 骨骼动画均由 CSMC 二进制资源转换
- 创造栏、背包和快捷栏继续使用已获许可刀包的专用 `slot_texture` 图标
- 显示使用玩家皮肤/衣物纹理的左右手与前臂
- 切换到刀具时播放 CSMC 的 `firstperson_draw` 动作
- 检视使用 CSMC 的 `firstperson_lookat01` 动作；蝴蝶刀按四个原始刚性网格分别跟随骨骼
- 使用“编辑物品”操作触发检视；电脑端默认按 `G`（可在键位设置中修改），手机端点击屏幕右侧的铅笔按钮
- 原版近战攻击与攻击动作/声音
- 自动向 `ScCsgoBox` 的荒野武器箱注册三把传奇刀具

## 构建

```powershell
dotnet build ScCsgoKnives.sln -c Release
```

产物位于 `src/ScCsgoKnives/bin/Release/ScCsgoKnives.scmod`。

## 授权

代码采用 GPL-3.0。手持模型、基础色贴图和骨骼动画移植自 CSMC；物品栏图标和声音来自 `[TaCZ X LR] CS2 Knifes Packet v1.0.1`。仓库维护者确认两部分均已取得修改与公开再发布许可，详情见 `THIRD_PARTY_NOTICES.md`。
