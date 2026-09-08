#!/usr/bin/env python3
"""Offline regressions for clean coats, source alpha, and atlas conventions."""
import tempfile
import unittest
import json
from pathlib import Path

import numpy as np
from PIL import Image

from build_gun_skins import clean_coat, load_rgb, pattern_colors, recipe_parameters, ROOT, TEX, sha256, gun_inputs
from gun_skin_reproject import raster_surface, lookup


class SkinBakeTests(unittest.TestCase):
    def test_painted_albedo_does_not_inherit_scratches(self):
        paint = np.full((2,2,3), .6)
        base = np.array([[[0,0,0], [1,1,1]], [[.1,.2,.3], [.9,.1,.8]]])
        np.testing.assert_allclose(clean_coat(paint,base,np.ones((2,2))), paint)
        np.testing.assert_allclose(clean_coat(paint,base,np.zeros((2,2))), base)
        np.testing.assert_allclose(clean_coat(paint,base,np.full((2,2),.5)), (paint+base)/2)

    def test_glb_atlas_is_y_down_and_pixel_centred(self):
        mesh = {"positions": np.array([[0,0,0],[1,0,0],[1,1,0],[0,1,0]]),
                "uv": np.array([[0,0],[1,0],[1,1],[0,1]]), "faces": np.array([[0,1,2],[0,2,3]])}
        positions, ids = raster_surface(mesh, 4)
        self.assertTrue((ids >= 0).all())
        np.testing.assert_allclose(positions[0,0], [.125,.125,0])
        np.testing.assert_allclose(positions[-1,-1], [.875,.875,0])
        image = np.arange(48).reshape(4,4,3).astype(float)
        np.testing.assert_allclose(lookup(image,positions[...,:2]), image)

    def test_packed_alpha_never_erases_rgb(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/"packed.png"
            Image.new("RGBA",(8,8),(200,50,100,0)).save(path)
            np.testing.assert_allclose(load_rgb(path,4), np.broadcast_to(np.array([200,50,100])/255,(4,4,3)), atol=1e-7)

    def test_repeat_samples_outside_tile_and_bilinear_seam(self):
        image = np.array([[0.,1.],[.25,.75]])
        uv = np.array([[.25,.25],[1.25,-.75],[-.75,2.25],[0.,.25]])
        np.testing.assert_allclose(lookup(image,uv,wrap=True), [0,0,0,.5])

    def test_native_export_preserves_uv_and_binding(self):
        from cs2_glb_to_obj import native_bindings
        report=json.loads((ROOT/"docs/gun-native-meshes-export.json").read_text("utf-8"))
        installed=json.loads((ROOT/"src/ScCsgoKnives/AnimationData/gun_native_meshes.json").read_text("utf-8"))
        for gun in report:
            self.assertEqual(gun["blended_vertices"], 0)
            self.assertEqual(gun["split_triangles"], 0)
            self.assertEqual(installed[gun["gun"]], native_bindings(gun))
            rig=json.loads((ROOT/f"src/ScCsgoKnives/AnimationData/{gun['gun']}.cs2.animation.json").read_text("utf-8"))
            for part in installed[gun["gun"]]:
                old=next((b for b in rig["Bindings"] if b["Bone"] == part["Bone"]),None)
                if old:
                    np.testing.assert_allclose(part["RightMatrix"],old["RightMatrix"],atol=3e-5)
        from gun_skin_preview import load_obj
        _,uv,_=load_obj(ROOT/"src/ScCsgoKnives/Assets/Models/ScCsgoKnives/ak47_legacy_cs2_clip.obj")
        self.assertLess(uv.min(),0)
        self.assertGreater(uv.max(),1)

    def test_pattern_mask_endpoints(self):
        pattern = np.array([[[0.,0,0],[1.,0,0]],[[0.,1,0],[0.,0,1]]])
        p = {f"g_vColor{i}":np.array(c) for i,c in enumerate([[.1,.2,.3],[1,0,0],[0,1,0],[0,0,1]])}
        v,u=np.mgrid[:2,:2]/2+.25
        np.testing.assert_allclose(pattern_colors(pattern,u,v,p), [[[.1,.2,.3],[1,0,0]],[[0,1,0],[0,0,1]]])

    def test_fade_recipe_overrides_template(self):
        paints=ROOT.parent/"CSMCReverse/local_cs2_analysis/all_weapons/10_paints"
        recipe=paints/"decompiled/weapons/paints/set_realism_camo/aa_fade_m4a1s.vcompmat"
        if not recipe.exists():
            self.skipTest("external CS2 export not installed")
        params,_=recipe_parameters(recipe,paints)
        self.assertTrue(params["TexturePattern"].endswith("/fade.psd"))
        self.assertIn("aa_fade_m4a1s_paint_by_number_masks",params["TextureMasks1"])
        self.assertEqual(params["g_flPaintRoughness"], .3)
        np.testing.assert_allclose(params["g_vColor1"], np.array([252,179,101])/255)

    def test_hd_hydroponic_magazine_preserves_factory_color(self):
        from build_gun_skins import adapt_hd_coat
        from gun_skin_reproject import correspondence, body
        from cs2_glb import Glb
        export=ROOT.parent/"CSMCReverse/local_cs2_analysis/all_weapons"
        if not export.exists(): self.skipTest("external CS2 export not installed")
        catalog=json.loads((ROOT/"tools/gun_skins_catalog.json").read_text("utf-8"))
        target=gun_inputs(catalog["guns"]["ak47"],export,64,False)
        # Deliberately all-magenta source: every magazine texel must still be original.
        v,u=(np.mgrid[:64,:64]+.5)/64
        out,mask=adapt_hd_coat(np.broadcast_to([1.,0,1],(64,64,3)),np.ones((64,64)),target,{"uv":np.stack([u,v],-1)},"am_bamboo_jungle")
        hd=body(Glb(target["glb"]),"hd"); _,ids=raster_surface(hd,64)
        clip=(ids>=0)&(hd["bones"][np.maximum(ids,0)]=="clip")
        self.assertGreater(clip.sum(),100)
        np.testing.assert_array_equal(mask[clip],0)
        np.testing.assert_array_equal(out[clip],target["color"][clip])

    def test_artwork_layout_separate_from_display_layout(self):
        catalog=json.loads((ROOT/"tools/gun_skins_catalog.json").read_text("utf-8"))
        for skin in catalog["skins"]:
            self.assertEqual(skin["legacyModel"],skin["paintId"]!=1177)
            self.assertEqual(skin["displayBody"],"hd" if skin["gun"]=="ak47" or skin["paintId"]==1177 else "legacy")

    def test_installed_skin_assets_and_unchanged_icons(self):
        catalog=json.loads((ROOT/"tools/gun_skins_catalog.json").read_text("utf-8"))
        report=json.loads((ROOT/"docs/gun-skins-assets.json").read_text("utf-8"))
        self.assertEqual(set(report["skins"]), {s["key"] for s in catalog["skins"]})
        self.assertEqual([s["paintId"] for s in catalog["skins"]], [51,756,344,724,180,456,302,984,946,1177,497])
        self.assertEqual(report["catalogVersion"], catalog["version"])
        for skin in catalog["skins"]:
            row=report["skins"][skin["key"]]
            self.assertNotEqual(row["mode"], "palette")
            for name,digest in row["files"].items():
                self.assertEqual(sha256(TEX/name), digest, name)
                with Image.open(TEX/name) as image:
                    self.assertEqual(image.size, (1024,1024), name)
            icon=row["unchangedInventoryIcon"]
            self.assertEqual(sha256(TEX/icon["file"]), icon["sha256"])
            normal=load_rgb(TEX/f"{skin['gun']}_hd__{skin['key']}_normal.png")
            self.assertEqual(row["body"], skin["displayBody"])
            if skin["displayBody"] == "hd":
                np.testing.assert_array_equal(normal, load_rgb(TEX/f"{skin['gun']}_hd_normal.png"))
            elif Path(row["normalSource"]).exists():
                n=load_rgb(Path(row["normalSource"]),1024)*2-1
                n/=np.maximum(np.linalg.norm(n,axis=-1,keepdims=True),1e-6)
                np.testing.assert_allclose(normal,(n+1)/2,atol=1/255)
            elif row["normalSource"] == "material default flat normal":
                np.testing.assert_allclose(normal,np.broadcast_to([128/255,128/255,1],normal.shape),atol=1/255)


if __name__ == "__main__":
    unittest.main()
