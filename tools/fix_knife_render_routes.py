"""One-time narrow edits; preserve the surrounding ongoing work."""
from pathlib import Path
R=Path(__file__).resolve().parents[1]/'src/ScCsgoKnives'
p=R/'Mod/ScCsgoKnivesModLoader.cs';s=p.read_text('utf-8-sig')
start=s.index('    // BlocksManager appends mod-defined categories')
end=s.index('    public override void ProjectXmlLoad',start)
s=s[:start]+'''    static void PlaceCreativeCategoryAfterWeapons() => ScCreativeCategoryOrder.Global();
    static void PlaceCreativeWidgets(Widget root) => ScCreativeCategoryOrder.WidgetTree(root);

'''+s[end:]
s=s.replace('using System.Reflection;\n','')
s=s.replace('        PlaceCreativeWidgetCategory(widget);\n        PlaceCreativeWidgets(widget);',
'''        if (widget is CreativeInventoryPanel panel) ScCreativeCategoryOrder.Inventory(panel.m_creativeInventoryWidget);
        else if (widget is CreativeInventoryWidget inventory) ScCreativeCategoryOrder.Inventory(inventory);''')
s=s.replace('CsmcFirstPersonRenderer.Draw(componentFirstPersonModel, camera, variant, pose)',
            'CsmcFirstPersonRenderer.Draw(componentFirstPersonModel, camera, variant, pose, itemValue)')
p.write_text(s,'utf-8')
p=R/'Rendering/CsmcFirstPersonRenderer.cs';s=p.read_text('utf-8-sig')
s=s.replace('public static bool Draw(ComponentFirstPersonModel firstPerson, Camera camera, int variant, KnifeRigPose pose)',
            'public static bool Draw(ComponentFirstPersonModel firstPerson, Camera camera, int variant, KnifeRigPose pose, int itemValue)')
s=s.replace('return DrawCs2(firstPerson, camera, variant, pose, post);','return DrawCs2(firstPerson, camera, variant, pose, post, itemValue);')
s=s.replace('static bool DrawCs2(ComponentFirstPersonModel firstPerson, Camera camera, int variant, KnifeRigPose pose, Matrix post)',
            'static bool DrawCs2(ComponentFirstPersonModel firstPerson, Camera camera, int variant, KnifeRigPose pose, Matrix post, int itemValue)')
start=s.index('        if (!CsmcKnifeRig.IsGun(variant) && !CsmcKnifeRig.IsGrenade(variant)) {',s.index('static bool DrawCs2('))
end=s.index('        var native',start)
s=s[:start]+s[end:]
s=s.replace('DrawCs2SkinnedWeapon(weapon, cs2, gun, post, projection, camera, in lighting, variant,',
            'DrawCs2SkinnedWeapon(weapon, cs2, gun, post, projection, camera, in lighting, variant, itemValue,')
s=s.replace('in KnifePbrRenderer.Lighting lighting, int variant, bool litGrenade)',
            'in KnifePbrRenderer.Lighting lighting, int variant, int itemValue, bool litGrenade)')
start=s.index('        if (!s_cs2WeaponBase.TryGetValue(asset, out Texture2D baseColor))')
end=s.index('        if (baseColor is null) return;',start)
s=s[:start]+'''        string bodyMaterial = ScKnifeSkinCatalog.MaterialForRender(asset, variant, itemValue);
        if (!s_cs2WeaponBase.TryGetValue(bodyMaterial, out Texture2D baseColor)) {
            try { baseColor = ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{bodyMaterial}"); }
            catch (Exception e) {
                KnifeDiagnostics.WarnOnce($"cs2-weapon-texture-{bodyMaterial}",
                    $"No CS2 texture for {bodyMaterial}: {e.Message}");
            }
            s_cs2WeaponBase[bodyMaterial] = baseColor;
        }
'''+s[end:]
s=s.replace('            Texture2D texture = key == asset + "_cs2" ? baseColor : PartBaseTexture(key);',
'''            if (key == asset + "_cs2") key = bodyMaterial;
            Texture2D texture = key == bodyMaterial ? baseColor : PartBaseTexture(key);''')
p.write_text(s,'utf-8')
p=R/'Rendering/ScThirdPerson.cs';s=p.read_text('utf-8-sig')
s=s.replace('? ScGunBlock.SkinOf(value) : 0;', '? ScGunBlock.SkinOf(value) : stance == ScThirdPersonStance.Knife ? ScKnifeBlock.SkinOf(value) : 0;')
s=s.replace('? ScGunBlock.SkinOf(held) : ScGunSkinCatalog.None;',
            '? ScGunBlock.SkinOf(held) : Terrain.ExtractContents(held) == BlocksManager.GetBlockIndex<ScKnifeBlock>(true) ? ScKnifeBlock.SkinOf(held) : 0;')
s=s.replace('? state.GunTexture : Load(group.Texture);',
'''? state.GunTexture : Load(group.Texture == state.Asset + "_cs2" && Terrain.ExtractContents(held) == BlocksManager.GetBlockIndex<ScKnifeBlock>(true)
                    ? ScKnifeSkinCatalog.Texture(state.Asset, skin, ScKnifeBlock.GetVariant(held)) : group.Texture);''')
p.write_text(s,'utf-8')
