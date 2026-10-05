# Windows worker恢复状态与E:/SD只读检查

时间：2026-09-28 23:24左右（Asia/Shanghai；VPS为+09时已到29日）。用户询问E:/SD用途及VPS中断报告。本轮只读诊断与本文记录，没有删除文件、启动构建、改服务/安全配置、安装或写世界。

## E:/SD

共5个文件，约23.32 GiB：Forge整合包sd-webui-forge-aki-v1.0.7z约2.45 GiB；FLUX.1-dev NF4模型约11.22 GiB；T5 XXL FP16编码器约9.12 GiB；CLIP-L约0.23 GiB；FLUX VAE约0.31 GiB。

它是本地AI绘图安装包及模型，不是Survivalcraft/CS2资源转换或C#构建依赖。当前项目src/tools/docs未查到E:/SD、sd-forge或stable-diffusion引用。我们当前用CS2原资源、模型导出/动画缓存、原生API构建，不需要此目录。若用户不用它做其他AI绘图，可以另行授权迁移/删除以释放空间；本轮保留未动。

## 已验证的恢复状态

- E盘现剩20530970624字节，约19.12 GiB；先前报告8.4GB是过去状态。
- Windows worker本地127.0.0.1:18765的认证POST health成功，约0.33秒；running_jobs为空，Everything就绪。
- 从SSH连接到VPS后实际执行win-worker health也成功，返回本机主机名和相同worker启动时间/磁盘信息。不是仅检查Windows进程存在。
- Worker进程仍为PID34688，启动于16:54；反向隧道PID2904启动于23:15。本轮没有重启它们。恢复机制已经生效；没有当时隧道错误日志，无法确定原始超时是网络中断还是其他瞬时原因。
- Syncthing项目源目录idle，本地need0/deletes0/errors=null；对端完成100%、need0/deletes0、remoteState=valid。此状态不等于确认VPS agent已经停止，继续写入前仍需确认单写入者。
- 独立SSH只读命令可执行，VPS根盘可用约61GiB。“agent shell safety check失败”原始错误不在本轮材料内，不能把它等同Linux磁盘满或worker鉴权坏，也没有禁用/绕过安全检查。若重试仍失败，需保留具体被拒命令、错误全文和会话工具名称再定位。

## 状态不明的清理实际已经成功

request_id：fu140-prune-02。
job_id：433bff7c8e8149abb64cee6f28123971。
持久记录status=succeeded、exit_code=0；日志stderr为空，stdout无截断。执行的是prune-fu140.ps1 f3-02，约23:07完成。

删除的是该旧测试轮的candidate、lite、full、assets-full、resources、tools、tree、baseline和air里的派生glb/scanim。结果日志显示释放约2.05 GiB，留下约9.1 MiB；本轮现场复核json、logs、motion、hotspots等仍在。这些是此前任务删除的可重建中间物，不是移入回收站；本次没有再删除，也不要重新提交同一清理。资源原始来源不是这些测试目录。

上一通过轮fu140-f3-03的job 7cab8d87db064be8b8c69065993e6017也确实为succeeded/exit0；日志列出36/36×2、split/native/motion/hotspots通过。最近嵌套Airborne规则和FollowupCheck/UI工具仍未经新Windows轮验证，不能复用f3-03的通过标记替它们背书。

## 给VPS的续跑指示

Windows和桥接当前已恢复。先读取本记录，重新检查health与同步并固定当前输入哈希；不要重复prune-02，不做额外磁盘删除。先评估f5-01是否已存在或同request_id是否已有任务，存在则查原job，不盲目重交。

按原授权范围用唯一request_id运行tools/dev.ps1 python tools/followup_140.py f5-01 all，或新未使用tag。当前E盘约19.12GiB，仍须检查预估新增空间和过程中余量，保留Syncthing的1%最低空间保护，不通过降低阈值绕过。

该轮应包含重组Airborne规则的引擎回归和性能复测、新设置/UI检查、预测与方向HUD渲染、Full/Lite+探员门禁；最后交Windows检查图像和实际游戏录像。若agent安全检查仍阻止工具，停止相关操作并返回原始错误，不禁用保护、不用另一个入口偷偷执行已被拒绝的命令。不得把离线图像说成实机录像，不写output/或安装，除非用户另行授权。
