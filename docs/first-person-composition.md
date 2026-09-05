# 第一人称里刀和手的关系（0.15.2；三把枪的 CS2 profile 见文末 0.16.x）

这份说明只讲"刀放在哪、手放在哪、两者怎么绑在一起"。渲染材质（PBR）不在这里。

## 一句话

刀按反编译出的 CS:MC 变换链原样摆放，用 CS:MC 给刀单独用的 48° 投影画；
拳头不走 CS:MC 的手臂盒，而是 0.13.x 的拳头求解器，贴在刀柄的握点上，
用和刀同一张投影画，所以拳头永远在刀柄上，屏幕上的大小和位置与 0.13.1 一致。

## 刀：CS:MC 的链，一步不改

`CsmcFirstPersonRenderer.ExactPlacement` 对每把刀每帧算一次，按 Engine 行向量的顺序：

```
网格(归一化) → 骨骼绑定 N⁻¹·Bone·N
            → S(f)            f = 本刀表头 ʠ / AK-47 的 ʠ（M9 0.406，爪子刀 0.260）
            → Rz270·Ry180·Rx90  CS:MC 的固定旋转
            → T(−0.22, 0.42, −0.18)   CS:MC 的固定平移
            → Rx(roll)        本武器注册行的 roll（刀 0，AK 0.73°）
            → T(lerp(hip, aim, 瞄准进度))   本武器注册行的偏移（M9 hip (0.2445, −0.3381, 0.1723)）
            → T(global)       全局视模偏移槽（设置 viewX/Y/Z，默认 0）
            → 走动/换手的机体运动（SC 自己的，等价于 MC 的 lag/bob）
```

**0.15.0 更正：没有"jar 字面里没有的项"。** 之前链里用的 hip (−0.1126, −0.4037, −0.0132) 和 roll −2.22° 来自注册表里唯一一行
`b$4bh`（带 `true` 标志的那行），解出字符串后它是 **双持 Beretta（elite）** 的左手枪配置，根本不是刀。每把武器（包括每把刀）
都有自己的 `b$4bg` 行：M9 是 hip (0.2445, −0.3381, 0.1723)、roll 0、只有 48 度一个视野。之前视频拟合出来的全局偏移
(0.36, −0.01, 0.185) 就是这两行的差，"取消网格中心项"补的则是各刀行之间 y 的差异（爪子刀 −0.4365、蝴蝶刀 −0.3905、M9 −0.3381，
差值和各自网格中心的平移一一对应）。现在链按字面走、全局偏移 0、网格中心项保留，M9 四帧刀尖 3–7 px、刀柄 2–24 px，
爪子刀和蝴蝶刀也落在视频上。全表在 `AnimationData/weapon_table.json`（`tools/apply_weapon_table.py` 从 jar 解出）。

枪的行（hip、瞄准、roll、腰射/瞄准视野）：AK-47 (0.2612, −0.4791, −0.0779) / (0.0647, −0.4211, −0.0542) / 0.73° / 48 / 27；
AWP (0.2648, −0.1763, −0.3559) / (0.073, −0.1277, −0.1642) / 0.43°；M4A1-S (0.2584, −0.3804, −0.1671) / (0.0451, −0.3334, −0.1671) / 0.31°。
瞄准进度把 hip 插到 aim、把 48 插到 27（`CsmcFirstPersonRenderer.AimProgress`，四分之一秒）。

投影：刀用 `ExactWeaponFovDegrees = 48`（CS:MC 每把刀的 FOV，刀族 48，乘 viewFov/70）在相机宽高比下建的透视矩阵，
近平面 0.05。这一项同时解释了 0.13.x 尺寸差 1.57 倍和"刀前半段往内偏"（48° 的透视更平）。

验收：`python3 tools/videocheck.py`，四帧刀尖误差 1–5 px，刀柄 5–15 px（1920×1084 的视频帧）。

## 手：0.13.x 的拳头求解器，挂在刀上

`DrawHands` → `DrawArm` → `SolveArm`，每帧：

1. **握点**：`RightGrip(variant)`（刀网格里标定的握点）经过"持刀骨骼绑定 × 刀的摆放矩阵"变到视空间。
   刀怎么动，握点就怎么动，拳头跟着刀走，这就是"拳头不会握到刀刃"的保证。
2. **肘部**：从**待机时的握点**（精确链算出来的 `s_exactIdleGrips`）沿手臂倾角向画面下方投影出去，
   肘固定在身体上不跟手走，所以举刀时手臂像前臂一样绕肘摆，而不是整根平移。
3. **拳头朝向**：宽面朝眼睛，再按手腕骨骼的实际 roll 转（`ArmRollMode`），检视定格时把刀柄贴在拳面上。
4. **尺寸**：`ScreenWidthFor` 给的是"占画面的比例"，按当前投影和拳头所在深度换算成视空间宽度。
   因为用的是画面比例，换成 48° 投影之后拳头在屏幕上的大小不变，仍是 0.13.1 的大小。
5. **左手**：待机时的左拳被钉到标定的屏幕位置（`SolveLeftHandCorrection`），动画只在此基础上动。

关键的一处：`SyncProjection` 在精确模式下把求解器用的投影 `s_projX/s_projY` 换成刀的 48° 投影，
拳头也用这张投影画。刀和手在同一个视空间、同一张投影里，握点在哪拳头就在哪。

CS:MC 原版的手臂是从固定视空间锚点拉到手骨的 MC 手臂盒（`ExactArmFrame`）。这条路保留着，
`ExactArms = 1` 可开；用 SC 的手模型画出来比 MCCS 大一倍多，0.14.0 就是这样，默认关。

## 深度：不清缓冲

MC 在手部通道前清一次深度让手永远在前。SC 的 `ComponentFirstPersonModel.Draw` 自己已经把视口
`MaxDepth × 0.1`，效果一样，所以我们什么都不做。0.14.0 在这里清了一次深度，结果绘制顺序更靠后的天穹
（SubsystemSky，序 5）把整张地形盖掉，就是"拿起刀世界变白"。

## 可调项（`ScCsgoKnivesTuning.txt`）

| 键 | 默认 | 作用 |
|---|---|---|
| `ExactChain` | 1 | 0 = 整个退回 0.13.1 的拟合构图 |
| `ExactWeaponFovDegrees` | 0 | 0 = 用表里的视野；>0 强制 |
| `ExactGlobalX/Y/Z` | 0 | 全局视模偏移（表值之外的附加量） |
| `ExactMeshCenterOffset` | 0 | 1 = 取消网格中心项（0.14.x 的做法，已不需要） |
| `ExactArms` | 0 | 1 = CS:MC 手臂盒 |
| `ExactHipX/Y/Z`、`ExactRollDegrees` | 0 | 加在表值上的偏移 |
| `ExactWeaponTX/TY/TZ` | jar 值 | 固定平移，一般别动 |

## 枪（0.15.0 起）

刀的链原样用于枪，外加：手用 CS:MC 的骨骼手臂（两只手各跟 `hand_r`/`hand_l` 骨，从固定锚点拉过来的盒子，宽度按拳头的屏幕比例）；
骨骼点用"绝对姿态 × 单位换算"的帧（`GetBoneFrame`），不带枪械绑定里的反向绑定矩阵，否则手会缩到原点。
枪口火光挂 `muzzle` 骨（M4A1-S 消音时挂 `muzzle2`）。

**0.15.1 更正（骨骼点的原点）**：0.15.0 的骨骼帧写成 `N⁻¹·abs·Left·N`，左边那个 N⁻¹ 带着 `translate(c)`，
于是帧的原点不是骨骼原点，而是"网格中心 c 经过这根骨骼"——AK-47 沿枪管偏 7 英寸（火光在枪口前方），
AWP 偏前 16.7 英寸、偏上 7.7 英寸（手和火光浮在镜筒上方，就是"AWP 手臂乱了"）。改成只保留单位换算 `scale(1/s)·abs·Left·N`
后，`muzzle` 骨落在两把枪网格枪口尖端 0.015 以内，左臂盒沿着 MCCS 录像里的袖子，右手在待机时位于画面下缘之外——
MCCS 的 AK-47/AWP 录像里待机时同样只看得见左袖。网格 part 仍用 `N⁻¹·Right·abs·Left·N`（顶点本来就在归一化空间，N⁻¹ 是对的）。AWP 开镜：瞄准进度到 1 后隐藏枪、画 `scope.png` 镜片、
世界视野按倍率缩（`SettingsManager.ViewAngle`），4 倍再按一次 8 倍。玩法数值在 `GunSpec.cs`，客户端里没有，先用 CS2 公开值。
## 曾经的未解之谜（已解）

0.14.x 里那个"刚好抵消 hip 和固定平移"的全局偏移，答案是链读错了行：读的是双持 Beretta 的家族行，
而每把刀有自己的行。见上面"0.15.0 更正"。反编译细节见 `CSMCReverse/work/firstperson-chain.md`。

## 0.15.2 更正（2026-09-04，按 0.15.1 实测反馈）

- **贴图花**：不是采样器。meshbin 的 V 是自上而下的（Source/DirectX），SC 的 OBJ 读取器按自下而上采样，
  所以枪的三张贴图全部上下镜像了（AK 木托变灰、弹匣变灰、受气盖上多出一块黄）。修法：转换器写 `vt u (1−v)`，
  已有 49 个 OBJ 全部就地翻转。刀也一样受影响，只是不明显（M9 刀身靠护手处的厂标此前被镜像到了刀柄上）。
  `tools/pbr_emulate.py` 用出厂的贴图、环境图和 BRDF 表离线复现 KnifePbr.psh，翻转前后的 AK 对比证实了这一点。
- **枪声**：引擎 `Ogg.OggStreamingSource.BytesCount = TotalSamples × 2` 只对单声道成立。立体声文件样本数为奇数时
  `SoundData` 直接抛 ArgumentOutOfRange（AWP、M4 的全部开火音、AK 的 fire_2、AWP 关镜、两段 AK 检视音都是这样），
  偶数时也只播前一半。所以全部 23 个立体声文件重编成单声道（libvorbis q7，原件留在 ~/csmc-guns/sounds/orig_stereo）；
  其中 ak47_fire_2 在 44.1 kHz 会让 NVorbis 挂死，改 48 kHz。`tools/SoundCheck` 用引擎自己的解码器逐个验证，48 个全过。
- **右键**：`OnAim` 在 InProgress 时返回 true，ComponentPlayer 把它当成"拒绝瞄准"立刻发 Cancelled，Completed 永远到不了。
  改为 InProgress/Cancelled 返回 false。
- **AWP 加载失败**（录屏标题里的 "failed to load awp: Index was outside the bounds"）：`SolveRollReferences` 对枪也走到了
  只有 22 把刀的 `s_handleDirections[variant]`。AK/M4 的 SolveArm 没成功所以没撞到，AWP 撞到了。加了 `HandleDirection()` 守卫。
- **手臂**：0.15.1 的臂盒到手腕为止，MCCS 的袖子（含手）一直到护木。现在盒子沿臂线延长到指根（rig 里 finger_middle_0 + 半段近节指骨），
  AK/AWP 待机叠到 MCCS 录像上盒端正好落在护木/前托。
- **枪口火光**：改用 CS:MC 自己的序列帧（CSMCTextureResources 的 particle/muzzle_flash：fire_gas_seq0 火球 32 帧 + wispy_steam_seq3 烟 26 帧），
  火球按 MCCS AK 录像量的尺寸（约 130 px/1920），加法混合；之后 0.45 s 的烟。CS:MC 每把枪用哪组序列在混淆代码里没读出来，
  三把枪目前同一组；消音 M4 只画小而暗的一团。
- **枪的环境光**：UV 修正后离线复现的 AK/AWP 仍比 MCCS 亮：沿枪管的掠射角把天空整个反射进来（AK 亮 25 %，AWP 亮 60 %）。
  CS:MC 的材质着色器是 CSMCMDL6 加密容器，按约束不解，所以改成按 MCCS 录像拟合：枪的环境光乘 `PbrGunEnvIntensity`，
  0.45 时 AK/AWP 枪身像素亮度与 MCCS 录像各差几个百分点（AK MCCS 0.30 / 我们 0.29；AWP 0.26 / 0.25，sRGB 亮度均值）。刀不变。

## 0.15.5：贴图"花"的真正答案（2026-09-04，F7 诊断图定案）

用户按 F7 截的五个视图（底色/法线/粗糙度/金属度/平法线）与离线复现逐通道做相关性：游戏里三张图和复现的 **v 自上而下**
采样通道相关 0.71/0.86/0.85，与 v-up 采样几乎为 0。结论：引擎按 v=0 在顶行采样，原始 OBJ 的 UV 是对的（0.15.2 的翻转是错的），
显卡读到的底色、粗糙度、金属度就是我们打包的图；法线路径也没坏（平法线视图同样"花"）。

那"花"是什么？把游戏帧和复现按同一掩码比亮度分布：均值/中位/标准差都吻合。差别在**贴图本身**：我们装的是 CS:MC 的 `native`
套，它就是 CS2 的 `*_default_color`（相关 0.994）——一张做旧底图，机匣盖上有黄色补丁、机匣和弹匣有锈斑，金属度几乎为 0。
MCCS 画的是 `tex/source2_vmat/<枪>/` 那套：干净的木头和钢（金属度 0.82）。用 AK 网格分别贴两套，右边就是 MCCS 的样子。
所以 AK 换成 source2_vmat（颜色/AO/粗糙度 R + 金属度 G/法线）。AWP 的 source2_vmat 只有灰色 `substrate` 底材，MCCS 的橄榄绿在
`native` 里，保留 native；M4A1-S 没有 source2_vmat 默认套，保留 native（MCCS 的 M4 也是这套的深色）。

环境光按 MCCS 三段录像重新拟合（引擎采样方向、镜头仰角 25°、枪身掩码亮度均值/中位）：`PbrGunEnvIntensity = 0.2`，
AWP 再乘 `EnvScale = 1.75`（0.35）。AK 0.256/0.255 对 MCCS 0.282/0.232，M4 0.211/0.191 对 0.209/0.181，AWP 0.238 对 0.238。

其它 0.15.5 修正：M4A1-S 消音器拆下后网格仍画着（`v_weapon_silencer` 从未按数据位隐藏，拆卸动画结束一回待机就"长回来"），
现在按方块数据隐藏，装回动画期间照画；这也是"装着消音器却是不消音枪声"的来源——数据早就是拆下状态，只是看不出来。

## 0.15.6：AWP 与 M4A1-S 也换成干净套（2026-09-04）

0.15.5 只换了 AK，用户反馈 AWP、M4 仍"不干净"。再查 `tex/source2_vmat/`：以 CS2 材质名命名的目录（`weapon_snip_awp`、
`weapon_rif_m4a1_silencer`、`weapon_rif_ak47`）装的就是 CS2 做旧默认图（与 native 同哈希）；以短名命名的目录（`ak47`、`awp`、
`rif_m4a1_s`）才是 CS:MC 画原版枪用的干净套。三把枪的网格分别贴上短名套，都是 MCCS 那种均匀深色。
- M4A1-S → `rif_m4a1_s/`（颜色/AO/粗糙度 R、金属度 G=0/平法线）。
- AWP → `awp/` 的 `substrate` 灰色底材，MCCS 的橄榄绿是再染色。染色量不从录像取（天空蓝会混进来），而是用 CS2 默认贴图里
  被漆成橄榄绿的像素除以底材同位置像素，线性光下 (0.586, 0.642, 0.387)，安装时乘在底色上（`--tint=awp:...`）。
- 环境光重拟合：`PbrGunEnvIntensity = 0.25`，AK `EnvScale = 0.8`。掩码内亮度均值/中位：AK 0.256/0.255 对 MCCS 0.282/0.232，
  M4 0.213/0.197 对 0.209/0.181，AWP 0.224/0.224 对 0.238/0.211。

## 0.15.7：以本地 CS2 导出件为准（2026-09-04）

Windows 侧用 Source2Viewer 把 CS2 全部 35 件枪械的资源离线导出到 `CSMCReverse/local_cs2_analysis/all_weapons/`
（数值、模型与动画、事件、两套材质、音效事件映射、粒子、开镜遮罩，校验 0 错误）。从这版起它是唯一权威来源，CS:MC 客户端包只用来核对。

- **材质**：CS:MC 短名目录那套干净贴图的哈希在 CS2 里全部命中，位于 `materials/models/weapons/v_models/<枪>/`，
  是 CS2 保留的 CS:GO 时代第一人称材质，原件 2048。`tools/install_gun_textures_cs2.py` 直接从导出件安装（缩到 1024），
  通道按各枪 legacy VMAT：颜色、AO、粗糙度取打包图 R、金属度取 G（VMAT 把同一张图绑为 g_tMetalness），M4A1-S 的 VMAT 把金属度写死 0。
  AK 的法线图在 2048 下也是平的（std 3）。
- **AWP 的橄榄绿**：legacy VMAT 的 tint 是白色，`awp_substrate_color` 本身是灰底材，Windows 侧"绿在贴图里"的说法不成立。
  底材的 alpha 通道标出裸金属件（枪管、镜筒、枪机、脚架为高，枪托机匣为 0），所以染色只乘在 alpha 为 0 的喷漆部位，
  金属件保持灰黑，与 MCCS 的"绿枪身黑镜筒"一致。染色系数仍是从 CS2 默认贴图/底材同位置像素比得到的 (0.586, 0.642, 0.387)。
- **枪声**：按 CS2 soundevents：AK = ak47_01/02/04（三个变体，无 03）；M4A1-S 不消音 = m4a1_01..04，也就是 M4A4 的同一批文件，
  这是 CS2 的原始映射；消音 = m4a1_silencer_01；AWP = awp_01/02；开镜和关镜都是 zoom.wav。文件改从 CS2 的无损 WAV 转单声道。
- **数值**：weapons.vdata 里还有后坐力（m_flRecoilMagnitude/AngleVariance）和散布（m_flInaccuracy*），GunSpec 里的踢枪数值仍是估的，待换算。
- 模型事件里没有音效事件（Windows 侧核实），换弹音的时间点仍用骨骼位移量出的值。
- 离线复现（引擎采样方向、仰角 25°）对 MCCS：AK 0.244 对 0.282，M4 0.215 对 0.209，AWP 0.229 对 0.238。
- **坑**：CS2 导出的 legacy 颜色 PNG 带一条几乎全 0 的 alpha（贴纸/喷漆辅助通道）。Pillow 对 RGBA 做缩放和中值滤波时先乘 alpha
  再除回来，alpha≈0 的地方颜色被除成噪点，离线复现里表现为木托上的红蓝麻点。安装脚本改为颜色按 RGB、alpha 按 L 分开处理，
  处理后与 CS:MC 的 webp 逐像素差 <2/255。

## 0.15.8（2026-09-04）

- 0.15.7 用户拿到的是噪点贴图：修好安装脚本后只重新打包没重新编译，打包脚本取的是编译输出目录里的旧贴图。0.15.8 重新编译，
  包内三张底色高频能量 1.2–1.6（噪点版 19.6）。规矩：改 Assets 必须 build 再 pack，每次重打包都 bump 版本。
- 开镜黑幕从 0.15.3 起其实一直没画出来：FlatBatch2D 的扇形三角绕向不一，被默认背面剔除吃掉，只剩十字线。改 CullNoneScissor。
- 开镜卡：AWP 瞄准渐变 0.25 s 内武器投影变好几步，每步 RebuildPlacements 把 22 把刀的检视 holds 全部重测（日志里成片的
  "projection changed / holds:"）。改为标脏，只在该 variant 下次绘制时重建。

## 0.15.9：AWP 开镜三处（2026-09-04）

- 对着天空黑幕变透明：黑幕原来在第一人称通道里画（序 1），天穹（序 105）随后盖在上面。现在由 SubsystemScGunBlockBehavior
  实现 IDrawable 在序 350（粒子 300 之后）画，需要 `SubsystemDrawing.AddDrawable` 手动注册。
- 开镜后原版准星被放大：SC 的准星是相机前 50 单位处一块固定尺寸的四边形，视野一窄它就变大。用 ModLoader 的 `IsCrosshairVisible`
  钩子在开镜时隐藏，黑幕自带一像素十字。
- 开枪要关镜拉栓：按 CS2，瞄准中开枪立即退出开镜播放拉栓，射击间隔（1.455 s）后若仍持枪、未忙、有弹，自动回到原倍率
  （手动切镜或换枪取消）。

## 0.15.10（2026-09-04）

- 原版准星仍在开镜时放大：0.15.9 写了 `IsCrosshairVisible` 钩子却没有 `RegisterHook`，SC 只对注册过的 loader 调钩子。已注册。
- 镜内十字线粗细改为可调 `ScopeLinePx`（默认 3 px @1080p，随画面高度缩放）。
- 开镜灵敏度：按 CS2 的 zoom_sensitivity_ratio 1.0，开镜时把 `LookSensitivity` 乘 1/倍率，退镜恢复。
- AWP 腰射时镜片是"电路板"：AWP 第二条身体记录（48 面，UV 在 u 1..2 那一格）就是镜片，它在枪身贴图上循环采到的是底材像素。
  现在按部件指定材质：`awp_lens`，取 CS2 `scope_awp.vmat`（默认色、金属度 0、scope 粗糙度）配 `shared/scope` 的贴图，深色玻璃。
- 刀的 OBJ 在 0.15.2/0.15.3 的翻转往返里只改了浮点格式，数值一致，已从 git 恢复原文件。


## 0.16.x：CS2 profile（2026-09-04）

三把枪（AK-47 / M4A1-S / AWP）多了一条完整的并行管线，用 CS2 本体的数据重做第一人称。
`ScCsgoKnivesTuning.txt` 里 `GunProfile = 1` 打开，`0` 退回上面那条 CS:MC 链。**默认仍是 0**，
因为叠合验收要的 CS2 录屏还没有。22 把刀完全不受影响，两条路都不碰它们。

### 与 CS:MC 链最大的不同：没有摆放要解

CS2 的 viewmodel 动画**就摆在相机的坐标系里**。从 clip 量出来的：`root_motion` 在原点，
枪身尾端 `wpnEnd` 在 x≈0，枪口在 +x 三四十英寸，y≈−5（Source 的 y 是左，负号=偏右），z≈−3（眼睛下方），
`trigger→muzzle` 指向 +x 到 0.05 以内——这就是标准 Source view space。

所以上面那条 `S(f)·Rz270·Ry180·Rx90·T·Rx(roll)·T(hip/aim)` 全部不需要，整条链只剩四步：

```
CS2 rig（英寸，Source 轴） → 坐标轴换（x前y左z上 → x右y上z后）
                          → ×0.0254
                          → viewmodel_offset（你本机的 2.5 / 0 / −1.5 英寸）
                          → 按 viewmodel_fov 68 建投影（Hor+ 换算成竖直 53.668°）
```

`Cs2Placement.cs`。cvar 是从 `D:\steam\userdata\1415980225` 的 `cs2_user_convars_0_slot0.vcfg` 读的，
不是默认值。唯一的假设是 Source 把 `fov` 当 4:3 下的水平视野、竖直角固定（Hor+）这条换算。

### 各阶段换掉了什么

| 部分 | CS:MC 链 | CS2 profile | 来源 |
|---|---|---|---|
| 动画 | CS:MC animbin | CS2 的二进制 DMX clip | `08_first_person/.../*.dmx` |
| 骨架 | 45 骨（无 meta/肩） | 64 骨（含 meta、肩、扭转） | 同上 |
| 网格 | `body_legacy`（AK 13435 面） | `body_hd`（AK 26133 面） | `02_models/.../weapon_*.glb` |
| 材质 | legacy v_models 贴图 + 平面法线 | 当前 VMAT 绑定的五张图 + 真法线 | `04_current_weapon_materials/` |
| 手臂 | Minecraft 式方块臂 + 拳头求解器 | CS2 的蒙皮手臂与无指手套（CPU 蒙皮 6274 顶点/帧） | `08_first_person/glb/.../weapon_arms.glb` |
| 火光/曳光 | 手调的时长与大小 | vpcf 的寿命/序列/颜色/透明度/淡出、vdata 的枪口位置与曳光频率 | `06_particles/`、`01_weapon_data/` |
| 开镜遮罩 | 对着 CS:MC 录像拟合的圆（0.4825 h / 0.0185 h） | CS2 HUD 的 `scope_circle.png`（0.475 h / 0.05 h） | `07_scope/panorama/` |
| 数值 | 手调的踢枪与散布 | vdata 的伤害/衰减/散布/后坐比例 | `01_weapon_data/` |

### 两条链共用的一件事

part 的摆放式子还是 `Right · boneAbsolute · Left`（`CsmcKnifeRig` 那条），只是 CS2 侧
`Left = 单位阵`、`Right = N⁻¹P⁻¹D_rest⁻¹`，输出英寸交给 `Cs2Placement`。
静息时 `Right·D_rest` 必须等于 `N⁻¹P⁻¹`，转换器里断言到 1e-8。

### 每一阶段都有自检脚本

`tools/cs2_rig_selftest.py`（动画）、`cs2_mesh_selftest.py`（网格材质）、
`cs2_placement_selftest.py`（摆放，比出厂 C# 与离线参照）、`cs2_arms_selftest.py`（蒙皮）、
`cs2_effects_selftest.py`（特效开镜）、`cs2_weapons_selftest.py`（数值）。
逐阶段的数字在 `docs/cs2-stage1..6-report.md`。

### 还欠的

**CS2 录屏**。阶段 1 的声音峰值、阶段 2 的亮度 5%、阶段 3 的地标 10 px、阶段 4 的手指 10 px
四条验收都等它；`tools/cs2_videocheck.py` 已经写好，量到的像素坐标填进 `LANDMARKS` 就能跑。
默认 profile 也等它才翻。
