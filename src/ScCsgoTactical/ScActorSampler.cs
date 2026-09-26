using System.Runtime.CompilerServices;
using Engine;
using Engine.Animation;
using Engine.Graphics;
namespace Game;

// Compile channel-to-bone bindings once. Playback, events and transition clocks
// remain native; sampling uses the native interpolation (including loop seams).
public static class ScActorSampler {
    sealed class Track {
        public ModelBone Bone;
        public ModelAnimation.AnimationSampler Translation,Rotation,Scale;
        public Matrix Rest;
        public Vector3 RestScale,RestTranslation;
        public Quaternion RestRotation;
        public void RefreshRest(){Rest=Bone.Transform;Rest.Decompose(out RestScale,out RestRotation,out RestTranslation);}
    }
    sealed class Clips {public readonly Dictionary<ModelAnimation,Track[]> Items=new();}
    static readonly ConditionalWeakTable<Model,Clips> models=new();
    public static void Clear()=>models.Clear();
    static Track[] Compile(Model model,ModelAnimation clip){
        var map=new Dictionary<string,Track>();
        foreach(var channel in clip.Channels){
            var sampler=channel.Sampler;if(sampler?.KeyTimes is not {Length:>0}||channel.Property==ModelAnimation.AnimationProperty.Weights)continue;
            var bone=model.FindBone(channel.TargetBoneName,false);if(bone==null)continue;
            if(!map.TryGetValue(bone.Name,out var track)){track=new(){Bone=bone};track.RefreshRest();map.Add(bone.Name,track);}
            switch(channel.Property){
                case ModelAnimation.AnimationProperty.Translation:if(sampler.Translations is {Length:>0})track.Translation=sampler;break;
                case ModelAnimation.AnimationProperty.Rotation:if(sampler.Rotations is {Length:>0})track.Rotation=sampler;break;
                case ModelAnimation.AnimationProperty.Scale:if(sampler.Scales is {Length:>0})track.Scale=sampler;break;
            }
        }
        return map.Values.ToArray();
    }
    public static void Sample(AnimationPlayer player,Matrix?[] output,float? sampleTime=null){
        if(player?.m_model==null||player.Animation==null||output==null)return;
        var clips=models.GetValue(player.m_model,static _=>new Clips());
        if(!clips.Items.TryGetValue(player.Animation,out var tracks))clips.Items.Add(player.Animation,tracks=Compile(player.m_model,player.Animation));
        float time=sampleTime??player.Time;
        foreach(var t in tracks){
            if(t.Rest!=t.Bone.Transform)t.RefreshRest();
            var s=t.Scale;var r=t.Rotation;var p=t.Translation;
            Vector3 scale=s==null?t.RestScale:player.SampleVector3(s.Scales,s.KeyTimes,time,s.Interpolation);
            Quaternion rotation=r==null?t.RestRotation:player.SampleQuaternion(r.Rotations,r.KeyTimes,time,r.Interpolation);
            Vector3 position=p==null?t.RestTranslation:player.SampleVector3(p.Translations,p.KeyTimes,time,p.Interpolation);
            output[t.Bone.Index]=Matrix.CreateScale(scale)*Matrix.CreateFromQuaternion(rotation)*Matrix.CreateTranslation(position);
        }
    }
    // The caller only enables transition sampling for its own controllers which
    // have never used root motion. Keep the native clocks, snapshots and blend math.
    public static void Compute(AnimationController controller,Matrix?[] output,bool transitions=false){
        var layers=controller.Layers;
        if(layers.Length==1){var layer=layers[0];var player=layer.Player;
            bool steady=layer.Weight==1&&layer.Transition==null&&!layer.m_activating&&!layer.m_deactivating&&player?.IsPlaying==true;
            if(layer.Driver==null&&layer.m_rootMotionConfig==null&&(steady||transitions)&&
                layer.BoneMask is not {Length:>0}&&layer.BoneMaskExclude is not {Length:>0}&&controller.m_ikSolver==null){
                Array.Clear(output);
                if(!layer.IsActive)return;
                if(layer.m_activating&&layer.m_activateSourceTransforms is {} activation){
                    Sample(player,output);float progress=layer.m_activateDuration>0?AnimationTransition.ApplyCurve(Math.Clamp(layer.m_activateElapsed/layer.m_activateDuration,0,1),layer.Curve):1;
                    for(int i=0;i<Math.Min(output.Length,activation.Length);i++)if(activation[i] is Matrix source)output[i]=AnimationLayer.BlendTransforms(source,output[i]??Matrix.Identity,progress);
                }else if(layer.Transition is {} transition){
                    var source=transition.m_sourceTransforms;float progress=transition.Progress;
                    if(transition.IsDeactivateTransition){
                        if(source!=null&&progress<1)for(int i=0;i<output.Length;i++)if(source[i] is Matrix value)output[i]=transition.BlendTransforms(value,Matrix.Identity,progress);
                    }else{
                        Sample(transition.TargetPlayer,output);
                        if(source!=null&&progress>0&&progress<1)for(int i=0;i<output.Length;i++)if(source[i] is Matrix value)output[i]=transition.BlendTransforms(value,output[i]??Matrix.Identity,progress);
                    }
                }else if(player?.IsPlaying==true)Sample(player,output);
                else if(player?.PreservePose==true&&player.HasValidAnimation)Sample(player,output,Math.Clamp(player.EndPhase,0,1)*player.Animation.Duration);
                else if(layer.m_holdPose&&player?.HasValidAnimation==true)Sample(player,output);
                if(layer.Weight<1)for(int i=0;i<output.Length;i++)if(output[i] is Matrix value)output[i]=layer.m_transition.BlendTransforms(controller.Model.Bones[i].Transform,value,layer.Weight);
                layer.AnimationPlayer?.SamplePointerTargets(controller.Model);layer.AnimationPlayer?.SampleMorphWeights(controller.Model);return;
            }
        }
        controller.ComputeBoneTransforms(output);
    }
}
