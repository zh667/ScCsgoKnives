# ScCsgoKnives

《生存战争 2》SCAPI 1.9.2.1 的 CS 风格近战刀具模组，作为独立内容模组与 `ScCsgoBox` 联动。

当前原型包含：

- 爪子刀
- M9 刺刀
- 蝴蝶刀
- 创造栏、背包和快捷栏使用原资源包的专用 `slot_texture` 图标
- 切换到刀具时的切刀动作
- 使用“编辑物品”操作触发检视；电脑端默认按 `E`，手机端点击屏幕右侧的铅笔按钮
- 原版近战攻击与攻击动作/声音
- 自动向 `ScCsgoBox` 的荒野武器箱注册三把传奇刀具

## 构建

```powershell
dotnet build ScCsgoKnives.sln -c Release
```

产物位于 `src/ScCsgoKnives/bin/Release/ScCsgoKnives.scmod`。

## 授权

代码采用 GPL-3.0。刀具模型、贴图、动画设计和声音移植自 `[TaCZ X LR] CS2 Knifes Packet v1.0.1`，由本仓库维护者确认已取得修改与公开再发布许可。详情见 `THIRD_PARTY_NOTICES.md`。
