# 阶段 2 报告：CS2 当前网格与材质

日期：2026-09-04。版本：0.15.12。上一版：0.15.11（阶段 1）。

## 1. 做了什么

1. `tools/cs2_glb.py`：glTF 2.0 / GLB 读取器（accessor、节点层级、mesh、skin 与逆绑定矩阵），
   矩阵按本仓库的行向量约定返回。阶段 4 的手臂与手套复用同一个。
2. `tools/cs2_glb_to_obj.py`：把 `body_hd` 按每个顶点所属的武器骨拆成刚性 part，写成引擎那个
   严格的 `ObjModelReader` 能读的 OBJ（每对象 ≤21845 面、≤65535 顶点、完整 p/t/n，超限按 `__c1/__c2` 切块）。
3. `tools/cs2_mesh_selftest.py`：五项验收，先于功能写，见 §3。
4. `tools/install_gun_textures_cs2hd.py`：按每把枪 `weapon_*.vmat`（着色器 `csgo_weapon.vfx`）的绑定装当前材质。
5. `tools/cs2_render_check.py`：离线渲一帧 CS2 网格 + CS2 材质，并预留 `--reference` 与 CS2 录屏比亮度。
6. `tools/pbr_emulate.py` 加了 `tex=` 选项（用另一套材质渲同一份几何）。
7. `<gun>.cs2.animation.json` 补上 `MeshParts` 与 `Bindings`：每个 part 绑到 CS2 自己的武器骨，
   用的是 `CsmcKnifeRig` 既有的 `Right * boneAbsolute * Left` 生产式。

## 2. 来源文件

| 用途 | 文件 |
|---|---|
| 网格 | `02_models/glb_with_animations/weapons/models/{ak47,m4a1_silencer,awp}/weapon_*.glb` 的 `body_hd` |
| 材质绑定 | `04_current_weapon_materials/weapons/models/<枪>/materials/weapon_*.vmat` |
| 贴图 | 同目录 `<枪>_default_{color,ao,rough,normal}.png` 与 `<枪>_default_<hash>_metal.png` |
| 骨架对照 | 阶段 1 的 `animation/anims/viewmodel/rifle/*/*.dmx` |

VMAT 绑定逐条落位（不是猜的通道顺序）：

    TextureColor1           -> <gun>_hd.png            RGB
    TextureAmbientOcclusion -> <gun>_hd_orm.png  R
    TextureRoughness1       -> <gun>_hd_orm.png  G
    TextureMetalness1       -> <gun>_hd_orm.png  B
    TextureNormal           -> <gun>_hd_normal.png

## 3. 验收数字

复现：`python3 tools/cs2_mesh_selftest.py`。完整输出 `docs/cs2-stage2-selftest.json`。

### A. GLB 绑定姿态 = 动画 DMX 的武器骨架

| 枪 | 共有骨 | 拟合缩放 | 残差最大 | 残差平均 |
|---|---:|---:|---:|---:|
| ak47 | 6 | 39.37009（1/0.0254） | 3.56e-07 in | 2.49e-07 in |
| m4a1s | 6 | 39.37009 | 1.92e-07 in | 1.19e-07 in |
| awp | 6 | 39.37009 | 2.46e-07 in | 1.42e-07 in |

残差是浮点级别。网格和动画对同一批骨的位置**完全一致**，缩放正好是米→英寸，旋转是干净的坐标轴置换。

### B. 刚性蒙皮

| 枪 | part 数 | 面数 | 权重非 1 的顶点 | 跨骨三角 |
|---|---:|---:|---:|---:|
| ak47 | 5 | 26166 | 0 | 0 |
| m4a1s | 6 | 45372 | 0 | 0 |
| awp | 6 | 39764 | 0 | 0 |

计划书 §3 预备了"权重不是 0/1 的顶点按最大权重归属，记录比例"和跨骨三角的撕裂问题：
**三把枪一个都没有**，每个顶点都 100% 属于一根骨。代码里保留了这两条规则，但它们没被触发过。

### C. 同一个转换器跑 `body_legacy` 对上仓库里已经在用的 OBJ

这是把坐标轴顺序、英寸换算和归一化钉死在一个**已经验收过**的资产上的检查。

| 枪 | part | 顶点 | 最大距离 | 平均距离 |
|---|---|---:|---:|---:|
| ak47 | weapon_hand_r | 6987 | 0.0065 mm | 0.0037 mm |
| ak47 | v_weapon_ak47_clip | 420 | 0.0061 mm | 0.0036 mm |
| ak47 | v_weapon_ak47_cliprelease | 56 | 0.0055 mm | 0.0037 mm |
| ak47 | v_weapon_ak47_bolt / trigger / __2 | 80/24/46 | ≤0.0059 mm | ≤0.0040 mm |
| m4a1s | 全 6 个 part | 118–11651 | ≤0.0140 mm | ≤0.0079 mm |
| awp | 除弹匣外 5 个 part | 50–10671 | ≤0.0200 mm | ≤0.0099 mm |
| awp | v_weapon_awp_clip | 382 | **1.3651 mm** | 1.0220 mm |

18 个 part 里 17 个对到 0.006–0.020 mm。唯一的例外是 AWP 弹匣：仓库里那份只有 382 个顶点，
GLB 里的同一根骨有 764 个，去掉平均偏移后残差仍有 2.04 mm——**CS:MC 用的是另一份弹匣网格**，
与转换器无关，已在自检里具名列为已知差异并带着它的数字。

于是同时得到：归一化空间用的 `MeshCenter` / `MeshNormalizationScale` 直接沿用各枪 `*.csmc.animation.json` 里的值，
CS2 网格因此落在与现在这份**完全相同**的归一化空间里，阶段 3 只需换摆放链，不用重新定标。
UV 也实测不需要翻 V。

### D. OBJ 合规

17 个 part 全部通过 `tools/validate_obj.py`（单对象、纯三角、完整 p/t/n、≤21845 面、≤65535 顶点）。
AWP 与 M4A1-S 的机身超过 21845 面，按既有约定切成 `__c1/__c2`。

### E. 材质

三把枪的 5 条 VMAT 贴图绑定全部在导出件里解析到文件：

| 枪 | AO 均值 | 粗糙度均值 | 金属度均值 | 法线偏离平面 |
|---|---:|---:|---:|---:|
| ak47 | 178.8 | 105.9 | 18.5 | 13.57/255 |
| m4a1s | 178.2 | 161.9 | 23.0 | 13.37/255 |
| awp | 180.0 | 118.5 | 15.7 | 11.38/255 |

法线偏离平面 11–14，是**真正的法线贴图**；现在包里那三张是纯平面（0.00）。

### 离线渲染（`tools/cs2_render_check.py`，idle 一帧，960×540）

| 枪 | 现有网格+材质 | CS2 body_hd + 当前材质 | 变化 |
|---|---|---|---|
| ak47 | 39830 px，亮度均值 0.5851 中位 0.6059 | 38862 px，0.4402 / 0.4280 | 像素 −2.4%，亮度 **−24.8%** |
| m4a1s | 45616 px，0.4339 / 0.4431 | 43744 px，0.4692 / 0.4533 | 像素 −4.1%，亮度 **+8.1%** |
| awp | 45493 px，0.4691 / 0.5258 | 51531 px，0.4109 / 0.4182 | 像素 **+13.3%**，亮度 −12.4% |

AK 亮度掉四分之一的原因是金属度：旧的那套从 legacy rough 贴图的 G 通道取金属度，可见面上均值 0.84；
当前 VMAT 绑定的 `TextureMetalness1` 均值只有 0.073。像素数差异说明两份网格的剪影确实不同（AWP 大 13%）。

**注意一个被否掉的做法**：一开始我用"现有几何 + 新贴图"来隔离材质变化，这是错的——
两份网格的 UV 排布实测完全不同（位置重合的顶点里，UV 差在 0.005 以内的占 **0%**），
那样会把新贴图采到错误的位置。上表两侧各用各的几何与各自的材质。

## 4. 未解决项

1. **计划书 §3 阶段 2.3 的"掩码内亮度均值/中位与 CS2 录屏差 <5%"没有完成**：需要你的 CS2 录屏。
   工具已经就位，`python3 tools/cs2_render_check.py --reference <帧>.png` 会直接打出百分比。
2. **AWP 底色偏中性**：当前材质的 `awp_default_color` 在可见面上 g/b = 1.077，而现在包里那张
   （legacy 灰底 + 拟合出来的橄榄绿倍率）是 1.209。哪一个更接近 CS2 里的样子要等录屏定。
3. **`g_flMetalnessTransitionBias` 未实现**（AK 2.0、M4A1-S 2.407、AWP 2.0）与 `g_vMetalnessRemapRange` [0,1]：
   `csgo_weapon.vfx` 没有反出来，消费这两个值的公式未知，所以只记录、不猜曲线。
4. **CS2 的 AK body_hd 没有独立的 `cliprelease` 几何**（骨归属只有 weapon_offset / bolt / clip / trigger），
   而现在的 mod 会动这个 part。切到 cs2 profile 时弹匣释放钮不再单独动，阶段 3 要处理。
5. **包体**：阶段 2 的产物 OBJ 9.1 MB + 贴图 9.7 MB 现在**没有打进包**（`csproj` 里 `CopyToOutputDirectory=Never`），
   因为阶段 3 之前没有代码画它们。阶段 3 打开后包体会从 29.4 MB 涨到约 48 MB；等阶段 7 删掉 CS:MC 那套之后约 43 MB。
   如果这个体积不能接受，可选项是法线贴图降到 512 或者只在 cs2 成为默认时才带 legacy 那套——**需要你定**。
6. **M4A1-S 的 `weapon_offset__c2` 在渲染检查里借用了 `__c1` 的矩阵**：同一根骨，矩阵本来就相同，不影响结果，
   但阶段 3 接真渲染器时要按 part 各自取。

## 5. 估计值清单

本阶段**没有引入估计值，并且去掉了一个**：

| 值 | 来源 | 是否估计 |
|---|---|---|
| 网格顶点/UV/法线 | GLB `body_hd` | 否 |
| 顶点归属骨 | GLB `JOINTS_0`/`WEIGHTS_0`（全部为 1.0） | 否 |
| 米→英寸 39.37009、坐标轴顺序 [2,0,1]、不翻 V | 对着已验收的 shipped OBJ 实测（§3.C） | 否 |
| `MeshCenter` / `MeshNormalizationScale` | 沿用各枪 `*.csmc.animation.json` | 否（沿用既有值） |
| 五张贴图的通道打包 | 各枪 VMAT 的绑定 | 否 |
| AWP 橄榄绿倍率 (0.586, 0.642, 0.387) | 旧管线里拟合出来的**估计值**，当前材质自带成品底色，**已不再需要** | 已移除 |
| `g_flMetalnessTransitionBias` | 记录未实现（见 §4.3） | 不适用 |

## 6. 不破坏既有路线的证据

| 检查 | 结果 |
|---|---|
| `tools/verify_cs.py` | 与 0.15.10 逐行相同，22 把全 ok，PASS |
| `tools/videocheck.py` | 与 0.15.10 逐行相同，最差地标 24.0 px |
| 包内容 | 0.15.12 与 0.15.11 逐条目 CRC 相同，**只有 `modinfo.json` 变了**（版本号），218 个条目不增不减 |
| 包体 | 29,443,186 B → 29,443,187 B |

阶段 2 不改变游戏里的任何表现，这是有意的：新网格和新材质要等阶段 3 的摆放链才画得出来。

## 7. 需要你做的事

只有一件，和阶段 1 是同一件：**CS2 离线对局录三段参考视频**（1920×1080、60 fps、关 HUD、带游戏内音频，
AK/M4A1-S/AWP 各一段「站定 → 打空一梭 → 换弹 → 检视」，站在光照均匀处）。
这一份同时用于：阶段 1 的声音峰值核对、阶段 2 的亮度 5% 验收、阶段 3 的叠合定标。
另外顺手把 §4.5 的包体取舍定一下。
