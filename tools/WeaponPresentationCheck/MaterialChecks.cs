using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using ZipArchive = System.IO.Compression.ZipArchive;

static class MaterialChecks {
    public static void Run(ZipArchive package,string output,Func<string,Texture2D> texture,Action<string,bool> check){
        var fixture=new BlockMesh();
        fixture.Vertices.Add(new(){Position=new(-.5f,-.5f,0),TextureCoordinates=new(0,0),Color=Color.White});
        fixture.Vertices.Add(new(){Position=new(.5f,-.5f,0),TextureCoordinates=new(2,0),Color=Color.White});
        fixture.Vertices.Add(new(){Position=new(0,.5f,0),TextureCoordinates=new(1,2),Color=Color.White});
        foreach(int i in new[]{0,1,2,0,2,1})fixture.Indices.Add(i);
        check("material/double-sided-normals-do-not-cancel",ScNpcWeaponRenderer.SurfaceNormals(fixture).All(n=>Vector3.Distance(n,Vector3.UnitZ)<1e-6f));
        var before=fixture.Vertices.ToArray();
        texture("env_specular_rgbm");texture("env_brdf");
        using var target=new RenderTarget2D(480,480,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
        Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,480,480);Display.ScissorRectangle=new Rectangle(0,0,480,480);
        var projection=Matrix.CreatePerspectiveFieldOfView(.8f,1,.01f,100);var background=new Color(22,27,34);
        bool simple=ScUiSettings.SimpleMaterials;
        try{
            foreach(var pair in new[]{("ak47",0), ("m4a1s",984), ("awp",344), ("deagle",962)}){
                string asset=pair.Item1,material=ScGunSkinCatalog.Material(asset,pair.Item2);bool legacy=ScGunNativeMesh.UsesLegacy(asset,material);
                using var stream=package.GetEntry("Assets/"+ScNpcWeaponGeometry.PathFor(asset,legacy)).Open();
                var geometry=ScNpcWeaponGeometry.Read(stream,asset,legacy);
                var color=texture(material);texture(material+"_normal");texture(material+"_orm");
                var box=geometry.Groups[0].Mesh.CalculateBoundingBox();foreach(var g in geometry.Groups.Skip(1)){var b=g.Mesh.CalculateBoundingBox();box.Min=Vector3.Min(box.Min,b.Min);box.Max=Vector3.Max(box.Max,b.Max);}
                float distance=Math.Max(.3f,(box.Max-box.Min).Length()*1.3f);
                var center=box.Center();var view=Matrix.CreateLookAt(center+new Vector3(distance*.75f,distance*.35f,distance*.7f),center,Vector3.UnitY);
                var light=new KnifePbrRenderer.Lighting{Intensity=1,Dir1=Vector3.Normalize(Vector3.TransformNormal(LightingManager.DirectionToLight1,view)),Dir2=Vector3.Normalize(Vector3.TransformNormal(LightingManager.DirectionToLight2,view))};
                var images=new List<Image>();
                foreach(bool simplified in new[]{false,true}){
                    ScUiSettings.SimpleMaterials=simplified;Display.Clear(background,1,0);var renderer=new PrimitivesRenderer3D();
                    foreach(var group in geometry.Groups){
                        string mat=group.Texture==asset+"_hd"?material:group.Texture;
                        var tex=group.Texture==asset+"_hd"?color:texture(group.Texture);
                        check($"material/queue/{asset}/{simplified}/{group.Bone}",ScNpcWeaponRenderer.Queue(renderer,group.Mesh,tex,view,15,legacy,null,mat,CsmcKnifeRig.KnifeCount+Array.FindIndex(GunSpec.All,g=>g.Name==asset),Matrix.Invert(view),light));
                    }
                    renderer.Flush(projection);var image=target.GetData(new Rectangle(0,0,480,480));images.Add(image);
                    check($"material/visible/{asset}/{simplified}",image.Pixels.Count(p=>p!=background)>500);
                    check($"material/queue-cleared/{asset}/{simplified}",renderer.m_allBatches.All(b=>b.IsEmpty()));
                    using var png=File.Create(Path.Combine(output,asset+(simplified?"-simple":"-pbr")+".png"));Image.Save(image,png,ImageFileFormat.Png,false);
                }
                check("material/pbr-active/"+asset,KnifePbrRenderer.Status=="active");
                check("material/pbr-and-simple-distinct/"+asset,images[0].Pixels.Zip(images[1].Pixels).Count(p=>p.First!=p.Second)>500);
            }
            check("material/geometry-unchanged",fixture.Vertices.SequenceEqual(before));
            ScUiSettings.SimpleMaterials=false;
            var fixtureTexture=texture("ak47_hd");texture("ak47_hd_normal");texture("ak47_hd_orm");
            var fixtureView=Matrix.CreateTranslation(0,0,-2);
            var fixtureLight=new KnifePbrRenderer.Lighting{Intensity=1,Dir1=Vector3.UnitZ,Dir2=Vector3.UnitY};
            var shared=new PrimitivesRenderer3D();
            Image DrawFixture(int actors){
                Display.Clear(background,1,0);
                for(int actor=0;actor<actors;actor++)
                    if(!ScNpcWeaponRenderer.Queue(shared,fixture,fixtureTexture,fixtureView,15,false,null,"ak47_hd",CsmcKnifeRig.KnifeCount,Matrix.Identity,fixtureLight))throw new Exception("PBR fixture fallback");
                shared.Flush(projection);return target.GetData(new Rectangle(0,0,480,480));
            }
            var reference=DrawFixture(1);
            int sharedCount=ScNpcWeaponRenderer.CachedMeshes;
            foreach(int actors in new[]{1,8,28}){
                DrawFixture(actors);var timer=System.Diagnostics.Stopwatch.StartNew();
                for(int frame=0;frame<10;frame++)DrawFixture(actors);
                Console.WriteLine($"material/crowd/{actors}: {timer.Elapsed.TotalMilliseconds/10:F3} ms/frame including GPU readback");
                check("material/crowd-shares-buffers/"+actors,ScNpcWeaponRenderer.CachedMeshes==sharedCount);
            }
            int count=ScNpcWeaponRenderer.CachedMeshes;check("material/shared-buffers",count>0);
            var owned=GraphicsResource.m_resources.Where(r=>r.GetType().DeclaringType==typeof(ScNpcWeaponRenderer)).ToArray();
            foreach(var resource in owned)resource.HandleDeviceLost();foreach(var resource in owned)resource.HandleDeviceReset();
            check("material/device-reset-keeps-shared-meshes",ScNpcWeaponRenderer.CachedMeshes==count);
            check("material/device-reset-restores-pixels",DrawFixture(1).Pixels.SequenceEqual(reference.Pixels));
            var other=new PrimitivesRenderer3D();
            ScNpcWeaponRenderer.Queue(shared,fixture,fixtureTexture,fixtureView,15,false,null,"ak47_hd",CsmcKnifeRig.KnifeCount,Matrix.Identity,fixtureLight);
            ScNpcWeaponRenderer.Queue(other,fixture,fixtureTexture,fixtureView,15,false,null,"ak47_hd",CsmcKnifeRig.KnifeCount,Matrix.Identity,fixtureLight);
            shared.Clear();check("material/camera-queues-independent",shared.m_allBatches.All(b=>b.IsEmpty())&&other.m_allBatches.Any(b=>!b.IsEmpty()));
            ScNpcWeaponRenderer.Clear();check("material/world-exit-disposes-buffers",ScNpcWeaponRenderer.CachedMeshes==0&&owned.All(r=>r.m_isDisposed));ScNpcWeaponRenderer.Clear();
            check("material/reentry-restores-pixels",DrawFixture(1).Pixels.SequenceEqual(reference.Pixels));
        }finally{ScUiSettings.SimpleMaterials=simple;Display.RenderTarget=null;}
    }
}
