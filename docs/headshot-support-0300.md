# 原版生物爆头支持清单（0.30.0）

来源：本机 `[Windows]SurvivalcraftAPI_1.9.2.1/Content.zip` 的 `Assets/Database.xml` 与 `Assets/Models/*.dae`，用 `Engine.Media.Collada` 解析（2026-09-07）。
规则：头区 = 挂在 `Head` 骨骼上的网格的包围盒，按当前姿态的骨骼矩阵变换；身体/腿/翅膀等其余网格盒参与"最近部位"判定。
鱼类模型没有 Head 骨骼（只有 Jaw），不判爆头。羊驼（Alpaca）模板引用的模型不在本机 Content.zip 里。

| 实体模板 | 模型类 | 模型 | 状态 |
|---|---|---|---|
| Alpaca | FourLeggedModel | Models/Alpaca | 模型不在本机 Content.zip，无法生成 |
| Alpaca_Shorn | FourLeggedModel | Models/Alpaca_Shorn | 模型不在本机 Content.zip，无法生成 |
| Alpaca_White | FourLeggedModel | Models/Alpaca | 模型不在本机 Content.zip，无法生成 |
| Barracuda | FishModel | Models/Barracuda | 鱼类：无 Head 骨骼，只算普通命中 |
| Bass_Freshwater | FishModel | Models/Bass | 鱼类：无 Head 骨骼，只算普通命中 |
| Bass_Sea | FishModel | Models/Bass | 鱼类：无 Head 骨骼，只算普通命中 |
| Bear | FourLeggedModel | Models/Bear | 骨骼头区 Head（盒 1×1×1 模型单位） |
| Bear_Black | FourLeggedModel | Models/Bear | 骨骼头区 Head（盒 1×1×1 模型单位） |
| Bear_Brown | FourLeggedModel | Models/Bear | 骨骼头区 Head（盒 1×1×1 模型单位） |
| Bear_Polar | FourLeggedModel | Models/PolarBear | 骨骼头区 Head（盒 26×41×26 模型单位） |
| Beluga | FishModel | Models/Beluga | 鱼类：无 Head 骨骼，只算普通命中 |
| Bird | BirdModel | — | 无模型名（抽象模板，不会生成） |
| Bison | FourLeggedModel | Models/Bison | 骨骼头区 Head（盒 43×39×28 模型单位） |
| Bull | FourLeggedModel | Models/Bull | 骨骼头区 Head（盒 47×32×34 模型单位） |
| Bull_Black | FourLeggedModel | Models/Bull | 骨骼头区 Head（盒 47×32×34 模型单位） |
| Bull_Brown | FourLeggedModel | Models/Bull | 骨骼头区 Head（盒 47×32×34 模型单位） |
| Bull_White | FourLeggedModel | Models/Bull | 骨骼头区 Head（盒 47×32×34 模型单位） |
| Camel | FourLeggedModel | Models/Camel | 骨骼头区 Head（盒 12×33×32 模型单位） |
| Camel_Saddled | FourLeggedModel | Models/Camel_Saddled | 骨骼头区 Head（盒 12×33×32 模型单位） |
| Cassowary | FlightlessBirdModel | Models/Cassowary | 骨骼头区 Head（盒 5×13×18 模型单位） |
| Cetacean | FishModel | — | 无模型名（抽象模板，不会生成） |
| Cow | FourLeggedModel | Models/Cow | 骨骼头区 Head（盒 34×32×28 模型单位） |
| Cow_Black | FourLeggedModel | Models/Cow | 骨骼头区 Head（盒 34×32×28 模型单位） |
| Cow_Brown | FourLeggedModel | Models/Cow | 骨骼头区 Head（盒 34×32×28 模型单位） |
| Donkey | FourLeggedModel | Models/Donkey | 骨骼头区 Head（盒 21×20×29 模型单位） |
| Donkey_Saddled | FourLeggedModel | Models/Donkey | 骨骼头区 Head（盒 21×20×29 模型单位） |
| Duck | BirdModel | Models/Duck | 骨骼头区 Head（盒 11×17×9 模型单位） |
| FemalePlayer | HumanModel | Models/HumanFemale | 骨骼头区 Head（盒 12×12×15 模型单位） |
| Fish | FishModel | — | 无模型名（抽象模板，不会生成） |
| FlightlessBird | FlightlessBirdModel | — | 无模型名（抽象模板，不会生成） |
| Giraffe | FourLeggedModel | Models/Giraffe | 骨骼头区 Head（盒 23×22×22 模型单位） |
| Gnu | FourLeggedModel | Models/Gnu | 骨骼头区 Head（盒 43×24×33 模型单位） |
| Horse | FourLeggedModel | Models/Horse | 骨骼头区 Head（盒 17×22×26 模型单位） |
| Horse_Bay | FourLeggedModel | Models/Horse | 骨骼头区 Head（盒 17×22×26 模型单位） |
| Horse_Bay_Saddled | FourLeggedModel | Models/Horse | 骨骼头区 Head（盒 17×22×26 模型单位） |
| Horse_Black | FourLeggedModel | Models/Horse | 骨骼头区 Head（盒 17×22×26 模型单位） |
| Horse_Black_Saddled | FourLeggedModel | Models/Horse | 骨骼头区 Head（盒 17×22×26 模型单位） |
| Horse_Chestnut | FourLeggedModel | Models/Horse | 骨骼头区 Head（盒 17×22×26 模型单位） |
| Horse_Chestnut_Saddled | FourLeggedModel | Models/Horse | 骨骼头区 Head（盒 17×22×26 模型单位） |
| Horse_Palomino | FourLeggedModel | Models/Horse | 骨骼头区 Head（盒 17×22×26 模型单位） |
| Horse_Palomino_Saddled | FourLeggedModel | Models/Horse | 骨骼头区 Head（盒 17×22×26 模型单位） |
| Horse_White | FourLeggedModel | Models/Horse | 骨骼头区 Head（盒 17×22×26 模型单位） |
| Horse_White_Saddled | FourLeggedModel | Models/Horse | 骨骼头区 Head（盒 17×22×26 模型单位） |
| Hyena | FourLeggedModel | Models/Hyena | 骨骼头区 Head（盒 17×21×17 模型单位） |
| Jaguar | FourLeggedModel | Models/Jaguar | 骨骼头区 Head（盒 18×23×19 模型单位） |
| Leopard | FourLeggedModel | Models/Leopard | 骨骼头区 Head（盒 18×23×19 模型单位） |
| Lion | FourLeggedModel | Models/Lion | 骨骼头区 Head（盒 29×33×35 模型单位） |
| MalePlayer | HumanModel | Models/HumanMale | 骨骼头区 Head（盒 12×12×15 模型单位） |
| Moose | FourLeggedModel | Models/Moose | 骨骼头区 Head（盒 53×32×37 模型单位） |
| Orca | FishModel | Models/Orca | 鱼类：无 Head 骨骼，只算普通命中 |
| Ostrich | FlightlessBirdModel | Models/Ostrich | 骨骼头区 Head（盒 5×9×15 模型单位） |
| Pigeon | BirdModel | Models/Pigeon | 骨骼头区 Head（盒 0×0×0 模型单位） |
| Piranha | FishModel | Models/Piranha | 鱼类：无 Head 骨骼，只算普通命中 |
| Player | HumanModel | — | 无模型名（抽象模板，不会生成） |
| Raven | BirdModel | Models/Raven | 骨骼头区 Head（盒 9×15×8 模型单位） |
| Ray | FishModel | Models/Ray | 鱼类：无 Head 骨骼，只算普通命中 |
| Ray_Brown | FishModel | Models/Ray | 鱼类：无 Head 骨骼，只算普通命中 |
| Ray_Yellow | FishModel | Models/Ray | 鱼类：无 Head 骨骼，只算普通命中 |
| Reindeer | FourLeggedModel | Models/Reindeer | 骨骼头区 Head（盒 45×20×27 模型单位），含角/耳，宽于头身，待实机校准 |
| Rhino | FourLeggedModel | Models/Rhino | 骨骼头区 Head（盒 25×54×34 模型单位） |
| Seagull | BirdModel | Models/Seagull | 骨骼头区 Head（盒 9×16×8 模型单位） |
| Shark_Bull | FishModel | Models/Shark_Bull | 鱼类：无 Head 骨骼，只算普通命中 |
| Shark_GreatWhite | FishModel | Models/Shark_GreatWhite | 鱼类：无 Head 骨骼，只算普通命中 |
| Shark_Tiger | FishModel | Models/Shark_Tiger | 鱼类：无 Head 骨骼，只算普通命中 |
| Sparrow | BirdModel | Models/Sparrow | 骨骼头区 Head（盒 0×0×0 模型单位） |
| Tiger | FourLeggedModel | Models/Tiger | 骨骼头区 Head（盒 18×23×19 模型单位） |
| Tiger_White | FourLeggedModel | Models/Tiger | 骨骼头区 Head（盒 18×23×19 模型单位） |
| Werewolf | HumanModel | Models/Werewolf | 骨骼头区 Head（盒 24×25×19 模型单位） |
| Wildboar | FourLeggedModel | Models/Wildboar | 骨骼头区 Head（盒 23×33×22 模型单位） |
| Wolf | FourLeggedModel | Models/Wolf | 骨骼头区 Head（盒 16×21×17 模型单位） |
| Wolf_Coyote | FourLeggedModel | Models/Wolf | 骨骼头区 Head（盒 16×21×17 模型单位） |
| Wolf_Gray | FourLeggedModel | Models/Wolf | 骨骼头区 Head（盒 16×21×17 模型单位） |
| Zebra | FourLeggedModel | Models/Zebra | 骨骼头区 Head（盒 15×23×22 模型单位） |

共 73 个模板；注册的头区模型 32 个（`ScHeadRules.VanillaHeadModels`，PackageCheck 会核对它与数据库推导集合一致）。
宽头（含角/耳）模型：Bull、Bison、Moose、Reindeer、Gnu、Cow、Lion 等的 Head 网格把角和耳一起框进去了，DAE 里角与头是同一个子网格，无法按材质拆分；
实机若在角上打出黄标，用 `ScHeadRules.Register("Models/Moose", new ScHeadRule(["Head"], new Vector3(0.5f, 1, 1)))` 这类按轴收缩来校准（估计值，需截图判断）。
