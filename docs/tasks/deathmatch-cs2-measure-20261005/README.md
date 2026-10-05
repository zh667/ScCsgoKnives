# CS2 实测证据（死亡竞赛第五轮，2026-10-04）

- **环境**：用户电脑上的 CS2 1.41.8.8/14188，完美世界客户端，离线 `-insecure`，只有 bot；地图 aim_map 和 aim_botz。经用户同意（"现在就可以"）。
- **原始帧与日志**：留在 Windows `E:/projects/ScCsgoKnives/.tmp/cs2-measure-20261004/`，不同步。
  - `spray-*.png`：扫射截图。
  - `console.log.measure-session`：本次会话的控制台记录。
  - `console.log.user-original`：用户原来的 console.log 备份，已还原回 CS2。
  - `sweep-log.txt`：命中盒扫描的控制台输出。
- `sprays.json`：9 把枪的逐发偏移角（度；俯仰向下为正、偏航向左为正）、拟合常数和预测验证。第一次法玛斯测量因相位相关锁错峰而作废，用两次重测代替。
- `hits.json`：26 条服务器命中日志（部位、伤害、护甲），每条都与 vdata 公式算出的伤害一致；另有 aim_map 的竖向扫描。
- **未测项**：
  - 移动速度：控制台输出延迟导致读数作废，采用 vdata 数值。
  - 停火后弹道复位的速度：采用 CS:GO SDK 规则。
  - 颈部是否吃护甲。
- 由此得到的实现和检查见 [round5](../deathmatch-addon-round5-20261005.md)。
