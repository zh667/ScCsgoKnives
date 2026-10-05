# 远古世界 0.41.16 接入 CS 携枪迁移的最小补丁建议（供审核，未实施）

日期：2026-10-02。编写：VPS。状态：**建议稿**。我们没有修改、重打包或安装远古世界；下面的改动需要其维护者采纳，或在用户另行授权后由我们做一份特定适配。在此之前，远古世界 0.41.16 与 CS 武器一起使用时，带枪穿越仍会出现 [通用迁移分析](universal-item-travel-analysis-20261002.md) §3 的结果（同号异型不可用、同号同型读成目标世界那把枪的状态）。

依据的包：`D:/下载/AncientWorld_v0.41.16.zip`（SHA-256 `e88b68f6…0977a66`），内含 DLL `Kelly.Survivalcraft.AncientWorld.dll`（SHA-256 `f9fe4ccf…b01df5b9`，Version 0.41.16）。行号指 Windows `.tmp/dev-temp/ancient-travel-20261002/source/Game/AncientWorldRuntime.cs`（反编译输出）。反馈者日志里的是 0.41.15，未取得其包，不假定两者逐字节相同。

## 1. CS 这一侧已提供的入口（候选 mpd 系列起）

`Game.ScGunTravelApi`（程序集 `ScCsgoKnives`），全部是静态方法，参数与返回值只用引擎和基础库类型，可像远古现有的 `AncientWildBondBridge` 那样按名称反射绑定，不需要引用 CS 的程序集：

```csharp
public const int Version = 1;                       // 字段常量（WildBond 用的是属性，这里用 GetField("Version").GetRawConstantValue()）
public const string Namespace = "zh667.ScCsgoKnives/guns";
string Export(IInventory inventory, string traveller, out string refusal);            // 来源世界：返回要随旅程保存的文本；null=没带 CS 枪，或被拒绝（refusal 说明原因）
bool   CanExport(IInventory inventory, out string refusal);                           // 来源世界：只检查，不产生文本
bool   CanImport(IInventory inventory, string envelope, out string refusal);          // 目标世界：只检查，不写入
bool   Import(IInventory inventory, string envelope, out Dictionary<int,int> values, out string refusal);
                                                                                      // 目标世界、恢复库存之前：写入携带的枪记录，返回“来源物品值 → 本世界物品值”
bool   Complete(IInventory inventory, string envelope, out string problem);           // 目标世界、恢复库存之后：核对物品与记录一致并标记完成
```

要点：

- 文本对提供方不透明（JSON，自带版本、namespace 与摘要），只需**原样保存并原样交回**。它只含旅行者携带的枪，不含整张枪表。
- `Import` 要么全部成功，要么什么都不写；同一份文本再次调用返回同一映射、不写任何东西（重试、崩溃后重放安全）。
- `Import` 的 `inventory` 传“即将被替换的那份库存”（旅行者自己的）：它里面旧的副本不算“别人还拿着这把枪”。
- 拒绝时（`refusal` 非空）**不要**按旧数值恢复 CS 枪：那正是今天的错误。其余物品照常。

## 2. 远古需要改的四处

### 2.1 新增可选桥（仿 `AncientWildBondBridge`）

```csharp
internal static class AncientCsGunBridge {
    const string Ns = "zh667.ScCsgoKnives/guns";
    static MethodInfo export, import, complete;
    public static bool Available { get; private set; }
    public static void Bind() {
        export = import = complete = null; Available = false;
        Type api = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Game.ScGunTravelApi")).FirstOrDefault(t => t != null);
        if (api == null || (int)api.GetField("Version").GetRawConstantValue() != 1) return;
        export = api.GetMethod("Export"); import = api.GetMethod("Import"); complete = api.GetMethod("Complete");
        Available = export != null && import != null && complete != null;
    }
    public static string Namespace => Ns;
    public static string Export(IInventory inventory, string traveller, out string refusal) {
        refusal = null; if (!Available) return null;
        object[] a = { inventory, traveller, null }; string text = (string)export.Invoke(null, a); refusal = (string)a[2]; return text;
    }
    public static bool Import(IInventory inventory, string text, out Dictionary<int, int> values, out string refusal) {
        object[] a = { inventory, text, null, null }; bool ok = (bool)import.Invoke(null, a);
        values = (Dictionary<int, int>)a[2]; refusal = (string)a[3]; return ok;
    }
    public static bool Complete(IInventory inventory, string text, out string problem) {
        object[] a = { inventory, text, null }; bool ok = (bool)complete.Invoke(null, a); problem = (string)a[2]; return ok;
    }
}
```

`Bind()` 与 `AncientWildBondBridge.Bind()` 在同一处调用。

### 2.2 `TravelerSnapshot`：能保存并原样带回“扩展项”

现状（已用实际 DLL 验证）：`InventorySnapshot.Load/Save` 会丢掉它不认识的 XML 节点，所以只往 XML 里塞东西没有用，必须由 `TravelerSnapshot` 自己读写。

```csharp
// TravelerSnapshot 增加：
public List<KeyValuePair<string, string>> Extensions = new List<KeyValuePair<string, string>>();

// Save()：在根元素里追加
//   <Extensions><Item Namespace="zh667.ScCsgoKnives/guns">…文本…</Item></Extensions>
// Load()：把每个 <Item> 读回 Extensions（**不认识的 Namespace 也保留**，再次 Save 时原样写出）
```

Schema 不必升：旧版本读到新文件会忽略该元素（但那样就等于没有桥，见 §4）。

### 2.3 `BeginTravel`（约 629 行）：在固定快照的同一处导出

```csharp
TravelerSnapshot travelerSnapshot = TravelerSnapshot.Capture(player);
// ↓ 新增：紧跟 Capture，在 SaveProject 之前
string refusal;
string guns = AncientCsGunBridge.Export(player.ComponentMiner.Inventory, player.PlayerData.Name, out refusal);
if (refusal != null) {                      // 例如：同一把枪叠放、有待结算击杀、记录缺失
    player.ComponentGui.DisplaySmallMessage("CS 枪械暂时不能随行：" + refusal, Color.White, false, false);
    s_transitioning = false; return;        // 不开始这次旅行；玩家处理后重试
}
if (guns != null) travelerSnapshot.Extensions.Add(new KeyValuePair<string, string>(AncientCsGunBridge.Namespace, guns));
```

导出只读取来源世界，不改它的枪表（只会把这几把枪的稳定身份记下，随后那次 `SaveProject` 一并保存）。

### 2.4 `TravelerSnapshot.Apply`（由 `OnPlayerAvailable` 约 825 行调用）：恢复库存前导入，恢复后确认

```csharp
public void Apply(ComponentPlayer player) {
    IInventory inventory = player.ComponentMiner.Inventory;
    string guns = Extensions.Where(e => e.Key == AncientCsGunBridge.Namespace).Select(e => e.Value).FirstOrDefault();
    if (guns != null) {
        if (!AncientCsGunBridge.Available)
            throw new InvalidOperationException("旅行者带着 CS 枪械，但目标世界没有可用的 CS 武器模组（或版本过旧）；旅行者文件已保留。");
        Dictionary<int, int> values; string refusal;
        if (!AncientCsGunBridge.Import(inventory, guns, out values, out refusal))
            throw new InvalidOperationException("CS 枪械未能迁入：" + refusal + "；旅行者文件已保留。");
        foreach (ItemStack stack in Inventory.Slots)                    // 把快照里的来源数值换成本世界的数值
            if (values.TryGetValue(stack.Value, out int mapped)) stack.Value = mapped;
    }
    Inventory.Apply(inventory);                                          // 原有逻辑不变（ResolveValue 仍按方块类型定位，映射后的值方块号已是本世界的）
    if (guns != null) {
        string problem;
        if (!AncientCsGunBridge.Complete(inventory, guns, out problem)) Log.Warning("[AncientWorld] CS guns: " + problem);
    }
    Clothing.Apply(player.ComponentClothing);
    …
}
```

抛出的异常沿用远古现有的失败路径（`OnPlayerAvailable` 的 catch、旅行者文件保留、下次进入重试）。`Import` 可重复调用，所以“已 Apply 但日志未标完成就退出游戏”的重放是安全的：第二次得到同一映射，不会再分配编号，也不会把枪在目标世界里已经发生的变化改回去。

## 3. 用实际 DLL 做过的离线验证

`tools/PackageCheck/ItemTravelRegression.cs` 的 `item-travel/ancient-world-0.41.16-real-assembly`（Windows 管线读取用户的 zip，仅在内存中取出 DLL，不解包资源、不写任何文件）：

1. **现状基线**：实际 `InventorySnapshot.Capture → Save → Load → Apply`，来源 #1 AK 7 发、方块号 304→320：目标 #1 是 Glock 时结果不可用；目标 #1 是 23 发的 AK 时读成 23 发。断言这两个错误结果**存在**（包若变了，此项会失败并提示基线过期）。
2. **按 §2 的三处调用加桥**（导出放在 Capture 旁、文本放在远古 XML 之外的扩展项里、Load 后先 Import 并改写快照里的数值、再用远古自己的 Apply、最后 Complete）：两种情况下枪都落到新的本地编号 #2、可用、读 7 发，记录行与来源完全相同，目标原有的 #1 不变。
3. 远古自己的库存 XML 会丢弃未知的 `Extensions` 节点（所以 §2.2 必须由 `TravelerSnapshot` 承担）。

结果登记在 verification 的 T02/T04/T05。这**不是**游戏内穿越验收：门/树门触发、真实存档读写、退出重进后的待处理旅程，都需要打了补丁的远古包和用户实机。

## 4. 边界与未覆盖

- 远古 0.41.16 只允许单玩家旅行；CS 核心按“每个旅行者一份文本”隔离（两名旅行者已离线验证），但我们没有绕过远古的单人限制，不宣称多人穿越可用。
- 旧版远古读到带扩展项的旅行者文件会忽略它并按旧数值恢复——等同没有桥。建议补丁版在读到自己不认识且标记为必需的扩展时拒绝恢复；CS 这份扩展应视为必需。
- 来源世界里枪的旧副本：远古离开前保存的玩家实体仍带着这把枪，返回时 `Apply` 会清空并重填旅行者库存，旧副本随之消失。若返回时同一把枪在该世界被**别的容器**持有，`Import` 拒绝（不覆盖别人手里的记录），旅行者文件保留。
- 已经损坏的枪（此前无桥穿越留下的串号/缺记录）不会被这套接口修复：恢复它们需要原始记录与来源证明，不能猜。
- 宠物（WildBond）、衣物、体征与 CS 无关，原有流程不变；CS 的导入失败会让整次 Apply 失败并保留旅行者文件，这是远古现有的失败处理，不是新的原子性保证。
