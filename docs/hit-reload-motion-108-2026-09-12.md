# CS 武器 1.0.8：龙的命中、移动持枪与换弹弹量

## 安装

全量版和 512 优化版二选一。解压 ZIP，把里面两个 `.scmod` 配套安装，移走旧的 CS 武器主包，避免同一模组装两份。资源包若已经是对应版本，可以保留原件：全量版资源 1.0.0，优化版资源 1.1.0，资源文件与 1.0.7 配套包逐字节相同。

两版使用同一个玩法 DLL。512 版保留此前的有损 WebP、模型优化和简化材质黑色修复。此次没有重新压缩图片、模型或声音。

## 改了什么，怎么检查

1. **火龙·斯莫德的躯干可以被 CS 射线命中。** 在 `survival` 预设下，用低等级枪朝龙的躯干射击，观察血量。此前把蒙皮模型的静态网格盒乘以父节点矩阵，得到极小盒子，落空后又禁止身体兜底；现在遇到 `Model.HasSkin` 就用实时身体碰撞盒进行普通命中。保留地形遮挡、最近目标、射程和龙自身的伤害上限，没有改成飞行实体子弹。
   - 这是身体碰撞盒兼容，不是完整的蒙皮表面命中。仅击中碰撞盒以外的翅膀、尾尖仍可能无伤；不推测蒙皮模型的头部，不额外赠送爆头。
   - 原生刚性模型继续保留网格判定，确定打空时不会强行判身体命中。`classic` 模式也不再用错误的蒙皮网格盒推测头部。
2. **枪和手臂一起轻微前后摆动。** 持枪走动、跑动、停止，再用加速效果测试。频率最高 1.55 次/秒，前后偏移最多约 0.024 格，移速更快不会继续加速晃动。停步、离地渐弱；开镜时幅度降至约 12%。保留切到原版物品时的下沉过渡。
   - 这是借鉴 CS2 感觉的平滑持枪运动，不宣称复刻 CS2 的全部运动参数。骑乘同样受频率限制；站在移动平台上按相对速度计算。
3. **弹匣装入后就更新弹量，换弹结束才可开火。** AK 从开始换弹约 1.10 秒更新弹量，约 2.43 秒结束动作。试着持续按住开火：插好弹匣后、拉栓完成前仍不应射击。
   - 各枪使用原有 `WPN_RELOAD_ADD_AMMO` 动画事件，不把所有枪强塞进同一个时间。双枪等到两边装填事件，机枪使用供弹完成事件；Nova、XM1014、截短霰弹枪继续逐发装入。
   - 插入前切枪/取消：保留旧弹量，不扣新弹药。插入后切枪/取消：保留已经装入的弹量与对应扣料；重新持枪仍要经过正常切枪动作。取消不会退回已经使用的弹匣，也不会重复加弹。

## 存档影响

物品布局仍为 v5，GunRegistry schema 仍为 6，成长规则仍为 4，35 枪型号编号不变。此次无需迁移或重新建世界，旧枪的身份、涂装、等级、计数器、耐久、消音器与已有弹量不重置；原有正式旧版本转换链保留。

只提前了正在换弹时的正常弹药事务提交时点。插入后保存，保存的是已装入弹量和已扣库存；插入前保存仍是原状态。枪械数据继续通过现有事务入口提交，没有新增存档字段，也不会凭空恢复早已丢失的历史记录。

## 自动验证与实机边界

验证针对 `.scmod` 内的真实 DLL，宿主引用 `D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1` 的游戏程序集。完整记录在项目 `output/fixes-1.0.8/`：包含命中/墙体距离限制、刚性网格打空、真实 Smolder GLB 蒙皮标记、不同帧率与极端移速、实际第一人称变换入口、换弹事件/开火锁、扣弹与取消、库存和注册表两轮 XML，以及用户发布的 1.0 包转换回归。

这些是 DLL 和数据验证，不是游戏实拍或 Android 设备验收。仍应按上面三步在游戏里检查龙的实际扣血、移动手感和音画时序。真实龙模型的自动测试读取 GLB 后检查身体兼容路径，没有加载整个恶灵传说战斗场景，也没有将身体盒测试称为完整蒙皮射线测试。

最终包内 DLL 检查结果：全量版 **20,434 项，0 失败**；512 优化版 **20,948 项，0 失败**。新增检查包括 62 组普通/空仓装弹事件与开火锁、31 枪装入后取消及库存/注册表两轮 XML、20 项命中与移动检查，另有 2 项事务时间边界自检。两版 DLL SHA-256 都是 `369462f785c5a5dc821903b4dc344d0c2eb077c5d9457ba63bd499d6a944fe6f`。

构建和重现检查（项目根目录 PowerShell）：

```powershell
./tools/dev.ps1 dotnet build src/ScCsgoKnives/ScCsgoKnives.csproj '-t:Rebuild' -c Release --nologo --verbosity quiet '-p:GameDllDirectory=D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1'
./tools/dev.ps1 python -X utf8 tools/pack_scmod.py --edition full --split-resources
./tools/dev.ps1 python -X utf8 tools/pack_code_update.py --version 1.0.8 --notes docs/hit-reload-motion-108-2026-09-12.md
./tools/dev.ps1 dotnet build tools/PackageCheck/PackageCheck.csproj -c Release --nologo --verbosity quiet '-p:GameDllDirectory=D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1'
./tools/dev.ps1 dotnet tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll --scmod output/ScCsgoKnives-1.0.8-preview.scmod --resource-pack output/ScCsgoResources-1.0.0.scmod --vanilla-content 'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Content.zip' --previous-growth-package 'D:\下载\CS枪械\[API1.9]CS武器1.0-作者ZH667.scmod' --skinned-model '..\analysis\snod-hit-20260912\evil\Assets\Models\Gltf\Smolder.glb' --json output/fixes-1.0.8/full-tests.json
```

512 版检查把 `--scmod` 换为 `output/ScCsgoKnives-1.0.8-Optimized512-preview.scmod`，`--resource-pack` 换为 `output/ScCsgoResources-1.1.0-Optimized512.scmod`，JSON 输出改为 `output/fixes-1.0.8/optimized-tests.json`。最终交付文件及环境指纹在同目录的 `delivery.json` 和 `final-verification.json`。

“射击水面没有水花”是独立的效果缺失：现有射线忽略液体，没有水面粒子事件。为了补水花无需全部改成原版 Projectile；本次未加入水花或改变弹道类型。
