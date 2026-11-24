using System.Text.Json.Nodes;
using Game;

static class LocaleChecks {
    public static void Run(string root,Action<string,bool> check){
        var old=LanguageControl.jsonNode;var english=LanguageControl.englishJsonNode;string language=LanguageControl.CurrentLanguageName;
        var categories=BlocksManager.m_categories.ToArray();
        try{
            LanguageControl.englishJsonNode=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"src/ScCsgoKnives/Assets/Lang/en-US.json")));
            foreach(string locale in new[]{"zh-CN","en-US","zh-CN"}){
                LanguageControl.CurrentLanguageName=locale;
                var json=LanguageControl.jsonNode=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"src/ScCsgoKnives/Assets/Lang/"+locale+".json")));
                var gun=new ScGunBlock{BlockIndex=510};var knife=new ScKnifeBlock{BlockIndex=511};
                for(int variant=0;variant<GunSpec.All.Length;variant++){
                    string expected=json["Blocks"][$"ScGunBlock:{variant}"]["DisplayName"].GetValue<string>();
                    check($"locale/{locale}/gun/{variant}",ScGunNames.Variant(variant)==expected&&ScGunNames.Of(GunSpec.All[variant].Name)==expected);
                    var snapshot=ScGunSnapshot.ForFresh(variant,true) with{CounterInstalled=true,AppliedGrowthLevel=3};
                    check($"locale/{locale}/counter/{variant}",ScGunNames.Item(snapshot)==expected+" · "+string.Format(json["ScCsgoKnives"]["Items"]["Counter"].GetValue<string>(),snapshot.Level));
                }
                for(int variant=0;variant<CsmcKnifeRig.KnifeCount;variant++){
                    int value=Terrain.MakeBlockValue(511,0,variant);
                    check($"locale/{locale}/knife/{variant}",knife.GetDisplayName(null,value)==json["Blocks"][$"ScKnifeBlock:{variant}"]["DisplayName"].GetValue<string>());
                    int skin=ScKnifeSkinCatalog.ForVariant(variant);
                    if(skin!=0){string name=knife.GetDisplayName(null,Terrain.MakeBlockValue(511,0,ScKnifeSkinCatalog.With(variant,skin)));
                        check($"locale/{locale}/knife-finish/{variant}",name.EndsWith(ScKnifeSkinCatalog.Name(skin,variant))&&(locale!="en-US"||!name.Any(c=>c is >= '\u4e00' and <= '\u9fff')));}
                }
                foreach(var skin in ScGunSkinCatalog.All)check($"locale/{locale}/finish/{skin.PaintId}",ScGunSkinCatalog.NameOf(skin.PaintId)==json["ScCsgoKnives"]["Finishes"][skin.PaintId.ToString()].GetValue<string>());
                Block[] blocks=[gun,knife,new ScAmmoBlock(),new ScGunSkinTemplateBlock(),new ScGunCounterTemplateBlock(),new ScC4Block(),new ScGrenadeBlock(),new ScChickenEggBlock(),new ScWeaponWorkbenchBlock(),new ScWeaponMaterialBlock(),new ScTacticalShieldBlock(),new ScTacticalBeaconBlock(),new ScTacticalDefuserBlock(),new ScTacticalSquadBlock(),new ScAirdropBlock()];
                foreach(var block in blocks)check($"locale/{locale}/category/{block.GetType().Name}",block.GetCategory(0)==ScWeaponText.Category);
                check($"locale/{locale}/category-label",LanguageControl.Get("BlocksManager",ScWeaponText.Category)==(locale=="en-US"?"CS Weapons":"CS武器"));
                json["BlocksManager"][ScWeaponText.Category]="Renamed category";
                BlocksManager.m_categories.Clear();BlocksManager.m_categories.AddRange([ScWeaponText.Category,"Terrain","Weapons","Other"]);ScCreativeCategoryOrder.Global();
                check($"locale/{locale}/renamed-filter-and-sort",BlocksManager.m_categories.SequenceEqual(new[]{"Terrain","Weapons",ScWeaponText.Category,"Other"})&&blocks.All(b=>b.GetCategory(0)==BlocksManager.m_categories[2]));
                json["Blocks"]["ScGunBlock:3"]["DisplayName"]="Locale reload sentinel";
                check($"locale/{locale}/no-cached-gun-label",ScGunNames.Variant(3)=="Locale reload sentinel");
            }
            LanguageControl.jsonNode=new JsonObject();LanguageControl.englishJsonNode=null;
            check("locale/missing-entries-safe",!string.IsNullOrWhiteSpace(ScGunNames.Variant(3))&&ScGunNames.Variant(-1)=="Unknown weapon");
            LanguageControl.jsonNode=JsonNode.Parse("{\"Blocks\":{\"ScGunBlock:0\":{\"DisplayName\":\"WRONG MODEL\"}}}");
            check("locale/missing-variant-does-not-use-variant-zero",ScGunNames.Variant(3)!="WRONG MODEL");
        }finally{LanguageControl.jsonNode=old;LanguageControl.englishJsonNode=english;LanguageControl.CurrentLanguageName=language;BlocksManager.m_categories.Clear();BlocksManager.m_categories.AddRange(categories);}
    }
}
