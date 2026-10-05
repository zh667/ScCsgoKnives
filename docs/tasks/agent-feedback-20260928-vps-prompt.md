# VPS agent启动提示词

供用户审阅规划后，另行下达实施任务时粘贴。本轮Windows仅诊断与规划，没有自动派发实现。

~~~text
你是VPS源码实现agent。按 docs/tasks/agent-feedback-20260928.md 实施这批探员改进，先复核Windows证据，不要把原因候选当已证实根因。

目录 /home/dev/source-sync/ScCsgoKnives。先读AGENTS.md、任务全文、证据摘要，以及docs/agent-guide的collaboration、compatibility、rendering-and-resources、build-and-release。Windows完整项目E:/projects/ScCsgoKnives，原资源E:/projects/CSMCReverse，Mods为D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods。

写代码前确认上一写入者停止、两端Syncthing无待同步/错误/冲突。VPS是源码同步树，不初始化Git、不pull/reset/restore、不回旧workspaces，不改已有Zeus等无关修改。检查win-worker health；资源解析/烘焙、完整构建走Windows worker，明确cwd、tools/dev.ps1和唯一request_id，重连查原job。禁止搬资源树到VPS或用占位资源绕过失败。

分批推进：P0同伴远距卸载/重生保留主人、精确装备、原枪ID、事务和死亡终态；P1阵营友伤、持续炸弹/火区避险、手动小队安全落点和准备期；P2用地形实测修台阶、队形和有限战术；P3玩家/敌方C4统一拆弹入口，创造直接5秒工具待遇，合法敌方拆弹的一次性奖励和持久收据；P4玩家成功投掷/同伴交互自动语音，分清开关冷却；P5跳跃腿部动画和具备地形接触、关节限制、性能上限的简化布娃娃。先解决数据安全，不因表现工作拖延P0。

血量、距离和预算是首轮实验值，依据测试调整并解释。不能给Owner=-1自动认主、猜装备补枪、清表/重排ID、升级补满状态，不能靠无敌/瞬移掩盖AI。维持布局6/schema7和兼容系列状态守恒，新同伴/区域/奖励字段必须经旧兼容保存验证。Full和Lite+探员一致，Lite无探员保留休眠数据，Mini不扩范围。禁止改第三方包、装用户Mods/设备、写原世界或自动snapshot。

补真实失败路径回归，尤其原生DespawnChunks/SpawnEntity，而不只组件Save；两次重进、损坏载荷、淡出死亡、唯一掉落、奖励失败不复制、三种输入和创造目录语义。手机性能、跳跃/死亡和语音听感交Windows实机验收，离线检查总数不是游玩证明。

本提示词授权源码/测试实现与Windows隔离候选构建验证。正式替换output或安装前，汇总具体候选路径、哈希、变更和未验收项供用户审阅。每批更新任务Results，游戏内名称描述只写通用功能。同步写入冲突先停相关文件；worker失联继续独立源码工作，记录阻塞，不扩大同步范围。无需重问方案文档已确定的常规实现细节。
~~~
