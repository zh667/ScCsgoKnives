# 加枪的管线：为什么枪和刀走不同的路

日期：2026-09-05。

## 一、先量再设计

给刀做的时候用的是"整把 CPU 蒙皮"。给枪照抄之前先量了一遍 32 把枪的 `body_hd`：

```
850,629 个顶点里，850,590 个只受一根骨影响、权重 1
真正需要混合的：39 个（全是 MAC-10 的背带）
跨两根骨的三角形：145 个（g3sg1 8、mac10 120、mp9 17）
对比手臂：6274 顶点里 70% 需要混合
```

**枪根本不是蒙皮网格，是一堆刚性件。** 所以按骨分组、每组一个矩阵直接画，
**逐顶点开销为零**；只有那 145 个三角形走蒙皮。

体积也是这条最省：

```
刚性件二进制   43.6 MB   (32 字节/顶点，不存骨索引和权重)
整体蒙皮       56.3 MB   (52 字节/顶点)
OBJ 分件      约 96 MB   (现有三把枪就是这么存的，3 MB/把)
```

## 二、`.cs2.parts` 格式

```
SCK2PART
u32  version = 1
u16  jointCount           name + 16f 逆绑定
u32  rigidVertexCount     pos 3f, normal 3f, uv 2f          32 字节
u16  rigidPartCount       u16 joint, string material, u32 indexCount, indices
u32  blendedVertexCount   pos, normal, uv, 4 骨索引, 4 权重   52 字节
u16  blendedPartCount     string material, u32 indexCount, indices
```

生成器带一条硬断言：**每个源三角形要么进刚性件、要么进混合残留**，
数不上就直接失败——静默丢三角会在枪身上开个洞。

绘制时每件的世界矩阵是 `inverseBind[joint] * boneAbsolute[joint] * placement * post`，
正好是蒙皮求和的单影响特例，所以现成的 `TryDrawSkinned` 原样能用，没写新的绘制代码。

## 三、动画名没有统一规律

CS2 的 clip 后缀不跟枪名走：

```
pistol_glock18  ->  draw_glock
pistol_hkp2000  ->  draw_hkp
rifle_ssg08     ->  draw_ssg08 和 draw_ssg08_lgcy 两套
```

所以后缀是**从每个目录自己的 draw clip 反推**的，其余动作按 `<动作>_<后缀>` 找。
34 个目录全部解析成功，四个核心别名（deploy/idle/shoot1/inspect）一个不缺。

## 四、动画骨架里没有的骨

M4A4 的网格有一块 `sight`，但它的 clip 骨架里没有这根骨——按"找不到就不画"的写法，
**准星会整块消失**。

正解是把它挂到武器根上，而且这在数学上是精确的，不是近似：顶点在绑定世界空间，
一个不参与动画的件相对武器根刚性固定，于是

```
InverseBind[j] * absolute[j]
  = InverseBind[j] * bindLocal(j->root) * absolute[root]
  = InverseBind[j] * inverse(InverseBind[j]) * InverseBind[root] * absolute[root]
  = InverseBind[root] * absolute[root]
```

`InverseBind[j]` 被约掉，直接用根骨的矩阵即可。自检把替换过的骨**报出来**
（`on the weapon root: [sight,ag1_hand_r]`），不让它悄悄发生。

## 五、玩法：三件容易做错的

### 音效不能按文件名猜

按 CS2 自己的 soundevent 映射（`05_audio/shoot-event-mapping.json`）解析：

```
USP-S   Weapon_USP.Single       -> usp_unsilenced_01..03    未消音
        Weapon_USP.SilencedShot -> usp_01..03               消音
```

文件名 `usp_01` 看着像"普通"，实际是**消音**的（USP-S 出厂带消音器）。按名字猜必错。

"每个音效有几个变体"也从代码里的写死表改成**扫描安装结果**生成
`cs2_sound_variants.json`——以前加一把枪要同时改代码，数对不上就播一个不存在的文件。

### 射击模式

`m_bHasBurstMode` 只有 **glock18 和 famas** 两把为真，节奏各不相同：

```
glock18   循环 0.500 s，发间 0.050 s
famas     循环 0.550 s，发间 0.075 s
```

一串三发**只算一次循环时间**（glock 单发 0.15 s，一串 0.5 s，不是 3×0.15）。
右键切换，和开镜、消音同一个键——没有任何一把枪同时具备其中两种，不会冲突。

**"3 发"是 CS2 数据里唯一没有的数**（只给循环时间和发间隔），标为 `BurstShotsAssumed`。

### 多弹丸

`m_nNumBullets`：Nova 9、MAG-7 和 Sawed-Off 8、XM1014 6，其余全是 1。
按单发做，霰弹枪会完全不对。

## 六、读 vdata 时踩到的两个坑

* **有些字段是数组**：Glock 的 `m_flCycleTime = [ 0.15, 0.3 ]`（主射击 / 连发）。
  按标量读会漏掉 5 把枪。
* **有些字段在 prefab 里**：`_base = "weapon_glock_prefab"`，要跟着继承链找。

修完之后 35 把枪只剩泰瑟枪缺散布/后坐——**它本来就不是火器**，读成 0 而不是编一个。

## 七、材质：三把枪有两个 vmat

AUG 和 SG 553 有瞄准镜镜片，泰瑟枪有充能表，各自是独立材质。
本体是**名字最短的那个**；镜片和充能表不单独安装，跟着枪身贴图走，
这和现有三把枪的做法一致。

（这一条是被自检拦下来的：原本"必须恰好一个 vmat"的断言直接挂了。如果当初写成
"取第一个"，AUG 就会拿镜片材质当枪身贴图，多半要到实机才发现。）

## 八、这一批加了哪八把

deagle、glock18、usp_silencer、m4a4、famas、mp9、p90、ssg08 ——
覆盖手枪 / 步枪 / 冲锋枪 / 狙击 / 消音 / 三连发各一类。

数值全部来自 `weapons.vdata`，`GunSpec` 里逐条标了出处。
变体号只往末尾追加（`FrozenOrder` 断言看着）。
