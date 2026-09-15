# 公测 1.0.0 → 当前包迁移风险核验（2026-09-15）

结论：正常计数成长路径的离线 DLL 迁移矩阵通过，但当前单独主包存在 CountOnly 迁移错误，且 output 中同名 ZIP 与单独主包不是同一版本内容。暂不建议把现有 output 整体作为已验收升级包发给玩家。未修改游戏 Mods、世界存档或生产源码。

## 来源确认与前次答复纠正

用户提供 D:/下载/[API1.9]CS武器1.0.0.scmod，与 D:/下载/CS枪械/[API1.9]CS武器1.0-作者ZH667.scmod 完全相同（330100611 字节）。

- 包 SHA256：9c700bc8133942ca4fe0ce7e9604296e7acd1533f684aaa5dd4bfda252e84424
- ScCsgoKnives.dll SHA256：31c737bb4b86a8f8d77dbaa3be89a3d47ae17aa8e515c07c05d9051b97e121d3
- 实际 DLL 常量：GunRegistry.Schema=4，GrowthRulesVersion=2，物品布局 v5。
- 旧包确实具有 GunRegistry、KillCount 及等级数据。前次答复称“没有可直接复用的现行 GunRegistry 结构、只能迁移 0.28.2”是错误的；0.28.2 专用迁移之外，本项目已有 schema 升级路径。

## 核验对象及交付不一致

1. output/ScCsgoKnives-1.0.14-preview.scmod：50 级，规则 7，schema 6。
   - 包 SHA256：a36a054aedea5b5fc739d9d9e0d4d2cbef6f7ae63802d9d4aaf31c338f51685f
   - DLL SHA256：e5b2ef321c1f28f419f9f474c764f3f44a214b0079d3f55301f092d5ffd7dad2
   - 资源依赖 [1.10.2,2.0.0)，本次实际测试搭配 ScCsgoResources-1.10.4.scmod。
2. output/CS武器-1.0.14-全量版.zip 内仍为 30 级、规则 4 主包和资源 1.10.1。
   - ZIP SHA256：58ac5b98d0e1ba3d4409c7b64d1f49a81b96ec473aa06b3590f59d8e4c8f29dc
   - 内主包 SHA256：b366ca0c70703d11ed9674db54b032a60e88d84d59b3c83b5d0d1ba78c553bd3
3. output/check-1.0.14-final.json 记录的主包 SHA256 为 a7fa8c3e848c4ad13c4c28616a266dc5a0dc56c753c9906497f2f91c3860380a，与以上两者均不同，不能充当当前单独主包的验收报告。

## 实际检查

通过 tools/dev.ps1 运行 PackageCheck，使用用户本机 API 1.9.3.1 DLL，--previous-growth-package 指向本次指定的 1.0.0 包。总计 18848 检查、0 失败，其中 growth30-boundary 10308 项，覆盖旧 DLL 生成的 35 型号、旧等级边界、空弹/半弹/满弹、损坏/部分/全耐久、消音器、采样皮肤、充能比例、两轮 XML、编号水位、原数据不变、跨世界生存/创造引用、备份失败拒绝和旧版拒读 schema6。

上述既有迁移矩阵主要使用 CountAndGrow，不能代替 CountOnly 检查。专项补查实际加载同一旧 DLL 与两份目标 DLL：

| 旧 AK 状态 | 当前单独主包结果 | 两轮重读 |
| --- | --- | --- |
| 成长模式，2000 杀，Lv20，7 发，1500/3000 耐久 | 2000 杀，Lv31，7 发，1447/2895 耐久 | 稳定、未隔离 |
| 成长模式，3000 杀，Lv30，7 发，1875/3750 耐久 | 3000 杀，Lv50，7 发，1875/3750 耐久，隐藏进度抵扣 2250 | 稳定、未隔离 |
| **仅计数模式，3000 杀，Lv0，7 发，750/1500 耐久** | **3000 杀，Lv38，7 发，1605/3210 耐久** | **错误状态会保存下来** |
| 无计数器普通枪，7 发，750/1500 耐久 | 原状态保持，新增格式字段 | 稳定、未隔离 |

CountOnly 根因：ScGunGrowthMigration.Convert 接收 grows 参数但未使用，无条件通过击杀计算等级并调用 ApplyLevel。世界 GrowthMode 仍是 CountOnly，却已写入成长等级。旧 ZIP 内规则 4 DLL 在相同专项样例中不会把 CountOnly 的 Lv0 提升。

## 风险与处理边界

- 当前不能宣称零风险。应先修复仅计数迁移、补相应回归，再使用新版本号统一主包/ZIP/资源依赖/报告，避免同名不同内容。
- 1.0.0 → schema6 需要完整世界备份；现有 schema 升级代码包含备份校验和失败拒绝，既有 DLL 检查也覆盖此流程。
- 升级后的世界不能直接换回 1.0.0：旧包会拒绝新 schema，回退需使用匹配旧包与升级前完整备份。
- 此前背包代理误分离已经耗尽的编号、已丢失的记录/旧等级不会由正常迁移自动恢复。止住继续误分配与恢复历史损失是两件事。
- 本次没有玩家完整故障世界，测试使用旧 DLL 生成的隔离夹具；不等于该玩家安卓端全部模组/世界实测。

证据保存在 docs/migration-100-audit-20260915/：完整检查 JSON、补充 DLL 样例输出、可复跑的独立探针代码与包哈希盘点脚本。
