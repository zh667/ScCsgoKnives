# 任务:修复 ScCsgoKnives 中 M9 检视动作「刀与手不同步转动」

你要修改一个把 CS:GO 刀移植进《生存战争》(Survivalcraft, SCAPI 1.9.2.1)的 mod。仓库在 `~/workspaces/ScCsgoKnives`,核心渲染文件是 `src/ScCsgoKnives/Rendering/CsmcFirstPersonRenderer.cs`。请先完整读它,不要凭本提示词猜实现。

## 症状(唯一目标)
M9 检视(inspect)动画播放时,**刀在匀速旋转,但第一人称的手/手臂不是和刀同步转的**,看起来两者脱节。此前已修好「手臂跟着刀转好几圈」的回归(见下),现在剩下的就是这个「转动不同步」。其它动作(待机、切刀、检视末尾定格)目前是好的,不要动它们。

## 关键架构:为什么会不同步
- **刀的网格**是用真实动画逐帧画的:`Draw()` 里 `pose.GetBinding(part.Binding) * root`(root = placement*post)。所以刀的旋转 = CS:MC 原始关键帧,精确无误。
- **手/手臂盒子**不是关键帧,是**合成**出来的:`DrawArm → SolveArm → ResolveRoll`。`ResolveRoll` 在当前模式(`ArmRollMode==1`)下的滚转 = 「把待机时朝向镜头的面,携带到手腕(hand_r)坐标系里,带一个淡入权重」+ 「`SquareAtHold` 无状态角度扭曲:滚转角一旦超过 `SquareFromDegrees`(约90°)就平滑推向 180°,到 `SquareFullDegrees`(约130°)推满」,最后还有一个 `RollSlewDegreesPerSecond`(约900°/s)的转速上限。
- **这就是不同步的根因**:刀走真实关键帧,拳头走这套启发式规则。检视 twirl 过程中手腕滚转角反复越过 90°,`SquareAtHold` 扭曲会在**动作中途**触发,把拳头朝 180° 拽,而刀还在匀速转 → 两者错开。请先在代码里确认这条假设,再动手。

## 地面真值(已验证,可直接采信,不要重新反编译去推)
CS:MC 提取包在 `~/workspaces/reference/csmc_ctf_20260902/deliverables/`,已核对准确。
- CS:MC 的手臂是 rig 自带的 `LeftArm/RightArm` 骨(只在 `first_person_rendering_sources/.../source2_arms.geo.json`,共 2 根),**滚转是逐帧烤进动画的,不是算出来的**;由 `first_person_rendering_sources/p007m6/AbstractC0150a0.java` 对这两根骨做 slerp。我们的骨架没有这两根骨,所以只能自拼盒子+规则,这个架构差异消除不了——目标不是复刻它的骨,而是让我们合成的拳头**和刀同步**。
- 刀绑在 `weapon_hand_r`;真手腕是 `hand_r`;真前臂是 `arm_lower_r`。
- m9 检视(6.2 秒)各骨**局部旋转累计行程**实测:`weapon_hand_r`=729°、`hand_r`=549°、`arm_lower_r`=30°。三者 net(首→末)都≈0,即 twirl 后转回原位。
- 数据在 `deliverables/animations/runtime_json/knife_m9.runtime.animation.json`,结构:`Clips["inspect"].Bones[骨名].Rotation = {Interpolation, Times, Values}`,`Values` 是四元数序列。可用它离线量任意骨在任意 clip 的真实逐帧朝向。

## 修改方向(建议,非硬性)
让检视 twirl **过程中**拳头的滚转和手腕(`hand_r`,即刀的旋转)**刚性 1:1 同步**,而把「末尾把持刀面摆正朝向镜头」和「刀柄贴合拳头面的 clearance」这类修正,**只在手腕静止下来的定格时**才施加。当前代码已用 `stillness` 门控 square,但基础滚转仍被 `SquareAtHold` 的无状态扭曲和淡入/转速上限带偏。核心是:动作中拳头跟随真实手腕滚转,不要在中途施加朝-180°的扭曲。请自行判断是收窄/门控 `SquareAtHold`,还是让基础滚转直接跟随 `hand_r` 的真实滚转,只要结果是「转动同步、末尾仍摆正」即可。

相关代码位置:`SolveArm`(算出 `rollFrame = GetBinding("hand_r")*placement` 并传给 `ResolveRoll` 作 `wrist`)、`ResolveRoll`(mode 1 分支:携带面 + `SquareAtHold` 扭曲 + 转速上限;mode≥2 分支还有 FollowHandle/ReGrip/square,当前不走)、`SolveRollReferences`(用 hand_r 记 `s_rollRef`)。热调参在 `KnifeTuning.cs`(运行时可读 `app:/ScCsgoKnivesTuning.txt`),相关键:`ArmRollMode, SquareAtHold, SquareFromDegrees, SquareFullDegrees, RollSlewDegreesPerSecond, InspectTravelScale`,以及常量 `RollFadeStart/RollFadeEnd`。以文件里的当前值为准,别信本提示词里的约数。

## 已修好、不要回退的东西
- 位置跟武器骨、滚转跟真手腕的解耦(0.11.13):`SolveArm` 里位置用 `RightWristBone/RightGrip`(→`weapon_hand_r`),滚转用 `rollFrame`(→`hand_r`)。这一步是对的,别把滚转又接回 `weapon_hand_r`(那会让手臂随刀转圈,行程 729° 是铁证)。
- 待机构图、左右手落位、刀柄贴合、切刀/检视末尾定格。

## 构建 / 打包 / 版本
- 构建:`DOTNET_ROLL_FORWARD=Major dotnet build src/ScCsgoKnives/ScCsgoKnives.csproj -c Release`(确认 0 error)。
- 打包:`python3 tools/pack_scmod.py`(有过期 DLL 守卫,必须先成功构建),产物 `output/ScCsgoKnives-<版本>.scmod`。
- 改 `src/ScCsgoKnives/modinfo.json` 的 `Version`(当前 0.11.13,请 +1)。

## 验证要求(重要)
- `python3 tools/verify_cs.py` 必须仍 **PASS**,且四把参考刀的 Chamfer/IoU 不变——它守的是待机构图,证明你没破坏静态摆放。
- **不要相信 `tools/fleet_qa.py` 判断本问题**:它的 `hold_side` 用 hand_r 复刻滚转,天然反映不出「同步」这个动态差别(历史上正因如此漏掉过回归)。它可跑但只作参考。
- 本问题的真正判据是**装机后录屏**:录 M9 检视,和 MCCS 参考视频比对「刀与手是否同步转动」。所以你的交付是一个可安装的 `.scmod` + 一句说明你改了什么、为什么,让用户装机录像确认。不要仅凭离线结果宣称修好。
- 不要改 `AnimationData/`:检视数据已确认与提取包一致,问题不在数据。

## 交付
改好后给出:改动的文件与函数、一句根因说明、构建+打包的实际输出、新版 `.scmod` 路径。不要提交 git,等用户实测通过再提交。
