# 方块索引冲突与枪的变体编号

日期：2026-09-05。依据：`~/workspaces/SurvivalcraftApi` 源码（不是反编译），
外加 SCIE 1.0.0.0、枪械Mod 2.8、黑蝎 2.8 三个实包的交叉验证。

## 一、SC 怎么分配方块索引

每个 `Block` 子类拿到一个 0..1023 的索引，写进世界的每一格。两种模式：

```csharp
// Survivalcraft/Block/Block.cs:27,29
public bool StaticBlockIndex = false;
public virtual bool IsIndexDynamic => !StaticBlockIndex;   // 默认动态
```

| | 怎么定 | 会不会撞 |
|---|---|---|
| **静态** | 类里写 `public static int Index = 500;` 且 `IsIndexDynamic => false` | **会**。两个 mod 写同一个数字，后加载的直接覆盖 `m_blocks[500]` |
| **动态**（默认） | API 从 `SurvivalCraftBlockCount + 1` 往上找空位 | **不会** |

动态分配的结果按**类名**存进每个存档：

```csharp
// Survivalcraft/Subsystem/SubsystemBlocksManager.cs
public override void Save(ValuesDictionary v) {
    foreach (var item in DynamicBlockNameToIndex) v.SetValue(item.Value.ToString(), item.Key);
}
public virtual void CallAllocate() {
    for (int i = SurvivalCraftBlockCount + 1; i < 1024; i++) {
        string blockName = m_savedValuesDictionary.GetValue(i.ToString(), string.Empty);
        if (!string.IsNullOrEmpty(blockName)) DynamicBlockNameToIndex[blockName] = i;
    }
}
```

所以一个世界会记住"索引 512 是 ScGunBlock"。下次即使装了别的 mod、本该分到别的号，
老方块也不会错乱。上限 1024，超了抛
`Too many blocks! Please reduce the mods count.`

## 二、我们现在是安全的，不用改

```
ScKnifeBlock / ScGunBlock:
  无 static int Index
  无 StaticBlockIndex
  无 override IsIndexDynamic
```

两个都走默认的动态分配，天然不与任何 mod 冲突。

## 三、SCIE 为什么会和别人冲突

SCIE 用**静态索引**，占了 500–528：

```
静态（IsIndexDynamic => false）:
  BaseItemBlock            Index = 500
  BaseNormalBlock          Index = 501
  BaseDeviceBlock          Index = 503
  BaseDamageableItemBlock  Index = 504
它声明过的全部 Index:
  500 501 502 503 504 511..520 523..526 528
```

任何把 `Index` 写死在这一段、并且设成静态的 mod，都会和它对撞。
**结论：不要用静态索引，除非有非用不可的理由**（例如要替换原版方块）。

## 四、还有一个隐患：类名是全局唯一键

这三处**都用简单类名，不是命名空间全名**：

```csharp
BlockNameToIndex[block.GetType().Name] = Index;          // BlocksManager.AllocateBlock
DynamicBlockNameToIndex[...GetType().Name] = num;         // 动态分配落盘
m_blocks.FirstOrDefault(v => v.GetType().Name == typeName) // CSV 的 Class Name 查找
```

所以两个 mod 各自定义一个叫 `GunBlock` 的类，**即使都用动态索引照样打架**——
其中一个的 CSV 会去配置另一个的方块。

我们叫 `ScKnifeBlock` / `ScGunBlock`，带前缀，风险很低。**新增方块一律带 `Sc` 前缀。**

## 五、枪的变体编号：只能追加

`GunSpec.All` 是个数组，**变体号就是数组下标**，而这个下标会写进存档的方块数据里。

```csharp
public static readonly GunSpec[] All = [ ak47, m4a1s, awp ];   // 0, 1, 2
```

往中间插一把枪，后面所有枪的号就全变了：老存档里的 AWP（2）会变成新插进来的那把。

**规则：只往末尾追加，永不插入、永不删除、永不重排。**
一把枪要下线就留着占位，不要从数组里拿掉。

这条已经写成自检断言（`gunspec/order`）：`All[0..2]` 必须仍是 ak47 / m4a1s / awp，
且数组长度只增不减。改动顺序会在离线阶段直接失败，不会等到实机。

## 六、变体域从 2 位扩到 6 位

枪的方块数据原本这么排：

```
位 8   7..2      1..0
   消音 弹数6位   变体2位 → 只能 4 把枪
```

35 把枪需要 6 位。直接扩会吃掉弹数的位，老存档的 AK 会读成不存在的枪 56。
所以加一个版本位：

```
位 14   13   12..6     5..0
   版本  消音 弹数7位   变体6位 → 64 把枪
```

读的时候先看 bit 14：是 1 按新排布读，是 0 就是老存档，按老排布读。
写的时候一律写新排布——所以任何一次改弹数或开关消音器，老数据就自动升级了。

空间够：SC 的数据域是 `(value & -16384) >> 14`（`Terrain.cs`），即 bit14..31 共 18 位，
但 bit31 是符号位（算术右移会变负），**安全可用 17 位**，新排布只用 15 位。

老值不可能误判成新排布：旧 `MakeData` 只用到 bit 0..8，bit 14 恒为 0。

## 七、边界条件（已定）

* **现在全力做全量版**（1024² 贴图）。轻量版等全量版做完再讨论。
* 拆包时两个发行版的**类名必须一致**（同一个 mod 的两个版本），
  但 `PackageName` 必须不同——咸鱼社区按 packageName 索引，撞了会互相覆盖。
