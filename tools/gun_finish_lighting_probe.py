"""Offline environment-factor A/B on the same M4 mesh/maps; not a game screenshot.

Uses KnifePbr.psh GGX, RGBM environment, LUT, AO and ACES equations. Orthographic
camera and face normals deliberately isolate lighting; this does not validate
game animation, tangent normals, user tuning or physical CS2 lighting parity.
"""
import json
import numpy as np
from PIL import Image, ImageDraw
from build_gun_skins import TEX, ROOT, load_rgb
from gun_skin_reproject import lookup
from gun_skin_preview import gun_mesh, render


def norm(v):
    return v / np.maximum(np.linalg.norm(v,axis=-1,keepdims=True),1e-9)


class LightingProbe:
    def __init__(self, material, factor):
        self.orm = load_rgb(TEX/f"{material}_orm.png")
        self.env = np.asarray(Image.open(TEX/"env_specular_rgbm.png").convert("RGBA"),float)/255
        self.brdf = load_rgb(TEX/"env_brdf.png")
        self.factor = factor

    def level(self,d,row):
        d = np.broadcast_to(d,(len(row),3))
        axis=np.abs(d).argmax(1)
        face=np.zeros(len(d)); uv=np.zeros((len(d),2)); mag=np.zeros(len(d))
        for a,pos,neg in ((0,(-d[:,2],-d[:,1]),(d[:,2],-d[:,1])),
                          (1,(d[:,0],d[:,2]),(d[:,0],-d[:,2])),
                          (2,(d[:,0],-d[:,1]),(-d[:,0],-d[:,1]))):
            for positive,coords in ((True,pos),(False,neg)):
                hit=(axis==a)&((d[:,a]>0)==positive)
                face[hit]=a*2+int(not positive)
                uv[hit]=np.stack(coords,-1)[hit]
                mag[hit]=np.abs(d[hit,a])
        uv=np.clip((uv/mag[:,None]+1)*.5,.5/128,1-.5/128)
        atlas=np.stack([(face+uv[:,0])/6,(row+uv[:,1])/6],-1)
        t=lookup(self.env,atlas)
        color=t[:,:3]*t[:,3:4]*6
        lum=color@np.array([.2126,.7152,.0722])
        return lum[:,None]+(color-lum[:,None])*.25

    def sample_env(self,d,rough):
        level=np.clip(rough,0,1)*5
        low=np.floor(level)
        return self.level(d,low)*(1-(level-low)[:,None])+self.level(d,np.minimum(low+1,5))*(level-low)[:,None]

    def __call__(self,color,uv,N):
        base=color**2.2
        orm=lookup(self.orm,uv,wrap=True)
        ao=orm[:,0]; rough=np.clip(orm[:,1],.04,1); metal=orm[:,2,None]
        V=np.array([0.,0.,1.])
        if N@V<0: N=-N
        noV=max(N@V,1e-4)
        f0=.04*(1-metal)+base*metal
        diffuse=base*(1-metal)
        brdf=lookup(self.brdf,np.stack([np.full(len(uv),noV),rough],-1))
        ibl=(self.sample_env(N,np.ones(len(uv)))*diffuse+
             self.sample_env(2*(N@V)*N-V,rough)*(f0*brdf[:,:1]+brdf[:,1:2]))*ao[:,None]*self.factor
        direct=np.zeros_like(ibl)
        for light in ([.12,.25,-.34],[-.12,.25,.34]):
            L=norm(np.array(light)); H=norm(L+V)
            noL=max(N@L,0); noH=max(N@H,0); voH=max(V@H,0)
            a2=rough**4
            D=a2/(np.pi*(noH*noH*(a2-1)+1)**2+1e-7)
            visibility=.5/(noL*np.sqrt(noV*noV*(1-a2)+a2)+noV*np.sqrt(noL*noL*(1-a2)+a2)+1e-7)
            F=f0+(1-f0)*(1-voH)**5
            direct+=((1-F)*diffuse/np.pi+F*(D*visibility)[:,None])*.5*noL
        linear=ibl+direct*(1-.5+.5*ao[:,None])
        return np.clip(linear*(2.51*linear+.03)/(linear*(2.43*linear+.59)+.14),0,1)**(1/2.2)


def main():
    rows=[]; metrics={}
    mesh=gun_mesh("m4a1s",True,True)
    for key in ("cu_m4a1s_printstream","cu_m4a1s_csgo2048","gs_m4a1s_snakebite_gold"):
        material="m4a1s_hd__"+key
        color=load_rgb(TEX/f"{material}.png")
        pictures=[]
        for factor in (.25,1):
            shader=LightingProbe(material,factor)
            im=render(*mesh,color,720,shader=shader).crop((0,180,720,520))
            pictures.append(im)
        row=Image.new("RGB",(1440,370),"white")
        for i,im in enumerate(pictures): row.paste(im,(720*i,30))
        d=ImageDraw.Draw(row)
        d.text((8,8),key+" | BEFORE env=.25",fill="black")
        d.text((728,8),"AFTER env=1 | offline PBR, face normals; same textures",fill="black")
        rows.append(row)
        before,after=(np.asarray(im,float)/255 for im in pictures)
        mask=(np.abs(before-.94)>.025).any(-1)
        metrics[key]={"beforeMean":float(before[mask].mean()),"afterMean":float(after[mask].mean())}
        assert metrics[key]["afterMean"]>metrics[key]["beforeMean"]
        # A white dielectric remains brighter, not an arbitrary recolored albedo.
    sheet=Image.new("RGB",(1440,370*len(rows)),"white")
    for i,row in enumerate(rows): sheet.paste(row,(0,370*i))
    sheet.save(ROOT/"docs/gun-skins-lighting-0385.png")
    (ROOT/"docs/gun-skins-lighting-0385.json").write_text(json.dumps({"limits":__doc__,"measurements":metrics},indent=2)+"\n","utf-8")
    print(metrics)


if __name__=="__main__":
    main()
