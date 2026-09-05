# M9 黄金基线对齐(第六轮)

真值:`CSMCReverse/runs/20260903-140032/controlled-m9-lookat01.jsonl` —— CSMC 5.10 原求值器对
`firstperson_lookat01` 在 [0,6.2s] 30Hz 采样 187 点、5 根存在骨的原始 attachment 姿态
(`Ӝ.þ(name)` = `RightMatrix × BoneAbsolute × LeftMatrix`,mesh/attachment 空间,未 normalize)。

## 黄金测试(可重复)

`python3 tools/golden_m9.py`:
- 跑生产 C#(`ArmPreview golden` → `CsmcKnifeRig.SampleRawBindings` 复用生产规则;`Sample().Bindings` 为 normalized),
- 逐 187 点、逐 5 骨(按名定位)、逐 m00~m33 元素 + 平移 + 旋转角对比,
- 报最大误差的 sampleIndex/time/bone/stage。

## 逐层结论

| 层 | 判定 | 证据/阈值 |
|---|---|---|
| **A 原始 attachment(动画+Binding)** | **通过(对客户端真值证明)** | 5 骨×187 点,最大元素误差 5e-6、最大平移 5e-6、旋转角 0°(误差仅 runtime JSON 小数截断);阈值 1e-4 |
| **B normalization / 坐标转换** | 通过(实现审查) | `0.0254` 只除在 attachment point 平移(Sample 附着点路径),不施于 Binding/旋转;`MeshCenter` 减一次、`Normalization=T(-center)·S(scale)` 作共轭 `InverseNorm·sourcePose·Norm` 施加一次、顺序正确;JOML↔Engine 元素序在 A 层已由数值吻合坐实无转置错配 |
| **C placement/握点/相机** | 通过(实现审查) | `s_placement=orientation·T(anchor−idleGrip)` 构造一次、anchor 只补偿一次;渲染 `GetBinding(part)·placement` 一次,无重复 model-space 修正、无二次转置 |
| **D 手臂后处理** | 候选(无数值真值可判) | CSMC 手臂是 LeftArm/RightArm(独立 geo,不在我们数据);第五轮真值只有 attachment 姿态、无最终相机/提交矩阵。滚转/SquareAtHold/clearance 是对"缺失手臂真值"的补偿,非可证 bug |

## 第一处偏差与是否修复

B1–B3 无可证实质偏差(A 层对真值精确,B/C 层实现正确)。剩余"画面不像"落在 D 层(手臂后处理)与最终相机/视投影,而这两处**在锁态下不产生、第五轮拿不到真值**。按任务"只有数值证据指出后处理破坏动画时才改",本轮**不凭感觉改 D 层**。

## D 层诊断开关(已存在,热重载 app:/ScCsgoKnivesTuning.txt)

- `SquareAtHold`:1 摆正 / 0 完全刚性(拳头严格跟真实 hand_r 滚转,不加摆正)。
- `SquareGateByStillness`、`SquareFromDegrees`、`SquareFullDegrees`:摆正的门控与展开。
- `ArmRollMode`、`RollSlewDegreesPerSecond`、`RollBlendDegrees`:滚转来源与限速。
- `InspectTravelScale`:检视回拉。
用 `SquareAtHold=0` 即可隔离"是否是摆正后处理覆盖了真实姿态"——需要最终相机/屏幕真值才能定量判 D 层,本轮不具备。

## 不变量

golden 测试 = SC 生产 rig 与 CSMC 客户端真值的可重复回归;`verify_cs.py` 仍守待机照片拟合(本轮 PASS,行为未变)。新增仅 `CsmcKnifeRig.SampleRawBindings`(工具用,复用生产规则)、`ArmPreview golden`、`tools/golden_m9.py`,不改渲染行为。
