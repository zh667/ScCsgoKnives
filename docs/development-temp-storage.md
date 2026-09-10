# 开发临时目录

本项目曾在 `Path.GetTempPath()/packagecheck-<包哈希>` 留存包内 DLL。
DLL 内嵌动画等资源，同一版本反复构建也会产生不同哈希目录；Full/Lite 的包哈希不同，过去各留一份。
生成的模拟世界 `sc-growth30-*`、`sc-live-backup-*`、`scgun-0282-tests` 也落在系统 Temp。
这会累积占用 C 盘，不是游戏存档所必需。用户清理后无法还原各目录原先的占用统计。

现在默认位置：`<项目目录>/.tmp/dev-temp`。
当前 Windows 项目为 `E:/Obsidian Document/Document1/ScCsgoKnives/.tmp/dev-temp`。

Windows 开发命令统一用：

```powershell
./tools/dev.ps1 dotnet build src/ScCsgoKnives/ScCsgoKnives.csproj -c Release --no-restore
./tools/dev.ps1 python tools/pack_scmod.py --edition both
./tools/dev.ps1 dotnet tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll --scmod output/ScCsgoKnives-0.41.10.scmod
```

入口仅修改子进程继承的 TEMP/TMP/TMPDIR，结束恢复调用进程原值，不改用户/系统环境变量。
PackageCheck 即便直接启动也自动使用项目盘；每次运行有独立目录，退出时清理，异常终止/文件仍占用时可能有残留。
需保留故障现场时设置 `SC_CSGO_KEEP_TEST_TEMP=1`；可用 `SC_CSGO_DEV_TEMP` 指定其它绝对目录。

此修改只影响开发工具，不修改或重打已发布模组、不移动游戏存档。
不涵盖其它应用的缓存、聊天附件、NuGet 缓存和 .NET SDK 安装位置，也不迁移现有的系统 Temp。
清理临时目录通常只是需要重新生成中间文件，但运行中的构建/安装器可能失败，截图附件和测试诊断也可能丢失。
不要把源码、游戏 Worlds、升级前 snapshot 备份当作临时文件删除。
