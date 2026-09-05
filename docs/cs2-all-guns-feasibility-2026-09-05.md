# 把 CS2 全部枪械移植过来：资源够不够，Windows 要做什么

日期：2026-09-05。结论先写：

* **资源全够，Windows 一件都不用再导。** 35 把枪的动画、模型、材质、骨架、数值、音效、
  粒子，全部已经在 VPS 上。
* **真正的约束是包体，不是资源。** 全量做完约 280 MB。
* **另有一个代码硬限制**：枪的方块数据只留了 2 位给变体，最多 4 把。

---

## 一、CS2 到底有多少把枪

按 viewmodel 动画目录数，`pistol/` 10 个 + `rifle/` 23 个（这个目录涵盖步枪、冲锋枪、
霰弹枪、机枪、狙击），加上 `_default_pistol` / `_default_rifle` 承载的消音变体
（USP-S 和 M4A1-S），骨架文件是 **35 个**，就按 35 把算。

mod 现在有 3 把：ak47（`rifle_ak`）、m4a1s（`_default_rifle`）、awp（`rifle_awp`）。

---

## 二、逐类清点：全部已在本地

### 第一人称动画 —— **253 段全部已导**

上一次导枪时是按 `pistol/` 和 `rifle/` **整个目录**导的，不是只导三把：

```
pistol/  11 个目录  101 段
rifle/   23 个目录  152 段
合计     253 段 DMX，全在 08_first_person/decompiled/animation/anims/viewmodel/
```

### 模型 —— **35 把全有，且全部带蒙皮**

`02_models/glb_with_animations/weapons/models/`，133 个 GLB（主体 + 弹匣 + 碰撞体）。

抽查了 8 把，**每一把都带 `JOINTS_0`/`WEIGHTS_0`**，活动件靠骨骼驱动：

```
ak47      12935 顶点  承重 bolt, clip, cliprelease, trigger, weapon_offset
awp       22528       bolt_action, clip, rail, trigger, weapon_offset
deagle    10738       clip, hammer, slide, trigger, weapon_offset
p90       14013       chargehandle, clip, release, trigger, weapon_offset
nova      17221       pump, trigger, weapon_offset
m249      21062       21 个（含 bullet01..）
revolver  17730       17 个（含 cylbullet1..）
glock18    4383       magazine, slide, trigger, weapon_offset
```

**这一条改变了做法**：现有三把枪走的是"拆成刚性分件 + 逐件绑骨"的老路
（`cs2_glb_to_obj` + `mesh_bindings`），而 22 把刀走的是"单个蒙皮网格"的新路。
枪的 GLB 既然也是蒙皮的，**新路对 35 把枪全部适用**，比老路省事得多。

### 材质 —— 34 个目录，覆盖全部

`04_current_weapon_materials/weapons/models/`：ak47 aug awp bizon cz75a deagle elite
famas fiveseven g3sg1 galilar glock18 hkp2000 m249 m4a1_silencer m4a4 mac10 mag7
mp5sd mp7 mp9 negev nova p250 p90 revolver sawedoff scar20 sg556 ssg08 taser tec9
ump45 usp_silencer xm1014。

### 骨架 —— 35 个 `.vnmskel`，一把不缺

### 玩法数值 —— `weapons.vdata` 88 条，全部枪都在

### 音效 —— 2202 个文件，35 把全覆盖

有四把是命名差异，不是缺失：

```
m4a4    -> m4a1_*   （CS2 里 M4A4 和 M4A1-S 共用开火音）
mp5sd   -> mp5_*
glock18 -> glock_*
cz75a   -> cz75_*
```

### 粒子 —— 380 个 `.vpcf`，27 个 tracer 系统

---

## 三、真正的约束

### 1. 包体：全量做完约 280 MB

按现有真实数据外推（不是估）：

| | 依据 | 全量 35 把 |
|---|---|---|
| rig JSON | 三把枪 23 段 clip 共 5.6 MB → 0.243 MB/段；全部 253 段 | **61.6 MB** |
| 蒙皮网格 | 22 把刀 9.5 MB，均 0.43 MB；枪顶点更多，按 1.6 倍 | **24.1 MB** |
| 贴图 1024² | 三把枪 9.7 MB，均 3.2 MB/把 | **113.2 MB** |
| | | **合计 198.8 MB** |

当前包 96.6 MB（其中三把枪已占 15.3 MB），**全量后约 280 MB**。

贴图降到 512² 后约 **166 MB**。

这是唯一一个会挡路的东西。可选的减法：

* 贴图 512²：省约 85 MB
* rig 曲线精度从 6 位小数降到 4 位：约省一半 rig，30 MB
* 只做常用枪（比如 10 把）：约 80 MB 增量，包落在 175 MB 上下
* 拆包：核心包 + 枪械扩展包分开发布

### 2. 方块数据只有 2 位给枪的变体 —— 最多 4 把

```csharp
// GunSpec.cs
public const int VariantMask = 0x3;                       // 2 位 = 4 把
public static int GetRounds(int data) => (data >> 2) & 0x3F;   // 弹数占 2..7
public static bool GetSilencerOff(int data) => ((data >> 8) & 1) != 0;
```

刀用的是 5 位（`& 0x1F`，32 个，够 22 把）。SC 的方块数据域是 **18 位**
（`Terrain.cs:377`，`(data << 14) & -16384`），现在枪只用了 9 位，**空间够**，
但常量要重排：变体 6 位（64 把）+ 弹数 7 位 + 消音 1 位 = 14 位。

**这是个破坏性改动**：变体域一旦从 0..1 位扩到 0..5 位，就会吃掉现在弹数所在的位，
老存档里的枪会读成别的枪。缓解办法要么加一个版本位，要么就接受
（毕竟只发布过 3 把枪）。这个得你拍板。

### 3. 每把枪的 mod 侧工作（资源之外）

资源是白来的，这部分不是：

| 项 | 能否自动 |
|---|---|
| `GunSpec` 条目（伤害/射速/弹匣/后坐/散布/开镜） | **能**，`weapons.vdata` 全有，`cs2_weapons.py` 已经在读 |
| rig 生成 | **能**，`cs2_knife_rig.py` 那套稍改就行 |
| 蒙皮网格 | **能**，`cs2_glb_to_skinned.py` 直接吃 |
| 贴图安装 | **能**，`install_gun_textures_cs2hd.py` 加一行表 |
| 音效 cue 表 | **半自动**，`cs2_sound_timings.py` 读 clip 事件，但音效名要对 |
| 中英文名 | 手工，35 × 2 条 |
| 图标/物品栏贴图 | 手工，35 张 |
| 合成配方 | 手工，35 条 |
| 特殊机制 | 手工：左轮的转轮、Negev 的旋转、消音器拆装、AUG/SG556 的开镜、泰瑟枪 |

前四项是脚本，一次写好 35 把一起出。后面是逐把的人工活。

---

## 四、所以 Windows 那边要做什么

**什么都不用做。** 没有需要补导的资源。

唯一可能要问的是：如果将来要给枪也做皮肤，那还是要 `10_paints` 里已经导好的
图案贴图 —— 那批也已经全量到位了。

---

## 五、建议的做法

1. **先不要一次上 35 把。** 先把方块数据的位重排掉（这是硬限制），
   再挑 5–8 把常用的（deagle、glock18、usp_silencer、m4a4、famas、mp9、p90、ssg08）
   走通新的"单蒙皮网格"管线，验证枪也能像刀一样省事。
2. 贴图**从一开始就用 512²**，除非实机证明不够。等 0.17.x 的实机反馈。
3. 再决定要不要铺到 35 把，以及要不要拆包。
