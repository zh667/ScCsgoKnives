# 无人值守MP测试：防火墙重复提示

Windows检查确认每轮测试的exe路径包含时间戳；防火墙现有授权分别对应每个旧case/server、client、client1、client2的Survivalcraft.exe，所以旧授权无法覆盖下一轮新路径。当前Windows agent没有管理员令牌。不能自动点击/绕过UAC、关闭整个防火墙或全局屏蔽通知。

Windows已准备一次性配置，尚未应用：E:/Develop/AgentBridge/Enable-ScMpTestNetwork.cmd（运行后用户确认一次UAC）；实际脚本Configure-ScMpTestNetwork.ps1支持-Preview和-Remove。规则只针对下列四个固定exe，允许本机UDP，明确阻止非回环入站；不允许局域网/互联网联机。这符合当前同机双端测试，远程联机另按实际范围配置。只有存在acceptance/sc-mp-network-setup.json且读回有效规则才算配置完成。

固定路径：E:/projects/ScCsgoKnives/.tmp/mp-test-runtime/{server,client,client1,client2}/Survivalcraft.exe。

VPS当前拥有tools/MpM0/m0.py写入权，Windows没有并发修改该脚本。请在下一次启动游戏前修改统一Game运行器：

1. 日志/报告仍在每轮case_dir，实际程序及doc/Mods放上述固定角色目录；不得使用玩家安装目录或世界。
2. 以OS锁独占整个固定测试环境，直到所有子进程真正退出才释放；另一个worker作业必须等待或有界报忙，不能覆盖正在运行的程序。
3. 每轮开始核验没有使用这些目录的旧进程，归档上一轮需要的日志/测试世界，再仅清理确认位于固定根内的测试状态/旧Mods并同步本轮引擎与Mods。不得删除源资源或玩家数据。记录每角色exe/DLL/Mods哈希以免固定路径复用掩盖旧输入。
4. 支持client与client1/client2这些已存在名称，禁止未经配置的新动态exe路径悄悄退回反复弹窗；同角色不能并发实例。
5. 审计TestAutomation：当前new UdpClient(cmdPort)实际上监听所有网卡，并非文档宣称的loopback-only。它支持EXEC/FUNC，不应广泛放行。隔离测试自动化组件应绑定IPAddress.Loopback并校验来源；这是测试基础设施修正，不能修改玩家平台/发布第三方包。游戏传输仍保持被测平台行为，本机测试由精确防火墙范围限制。
6. 完成后先跑一组短的server+client本机通信，再连续运行两轮证明exe路径稳定、无交互等待、每轮状态独立。不能仅凭规则已创建宣称不会弹窗。未配置管理员规则时明确预检失败，不拉长超时盲等。

用户本次授权目标是减少网络弹窗并允许无人值守测试；不扩大联机玩法范围，不自动发布。本说明不表示已经向VPS发消息或修改它的活动源码。
