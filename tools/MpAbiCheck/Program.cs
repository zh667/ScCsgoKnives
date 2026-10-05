// current-direction-20260929 §6 M0: binary interface of the delivered mod assemblies against another engine build.
// Every type and member reference our DLLs make into the engine assemblies (Survivalcraft, Engine, EntitySystem, ...)
// is looked up by name and signature shape in the target build's assemblies, from metadata only (nothing is loaded
// or executed). Reports what would fail to bind at run time (missing type, missing member, changed signature).
// Also lists every assembly each mod references and whether the target build (or the shared framework) has it.
// Usage: MpAbiCheck <report.json> <target assembly folder> <mod dll>...
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

if(args.Length<3)throw new ArgumentException("MpAbiCheck <report.json> <target assembly folder> <mod dll>...");
string report=args[0],folder=args[1];
var engineNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Survivalcraft","Engine","EntitySystem"};
// Target build: every public-or-not type with its members, keyed by assembly + full name.
var targetTypes=new Dictionary<string,(Dictionary<string,List<string>> Members,string BaseType)>(StringComparer.Ordinal);
var targetAssemblies=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
foreach(var path in Directory.GetFiles(folder,"*.dll")){
    using var pe=new PEReader(File.OpenRead(path));if(!pe.HasMetadata)continue;var md=pe.GetMetadataReader();if(!md.IsAssembly)continue;
    string asm=md.GetString(md.GetAssemblyDefinition().Name);targetAssemblies[asm]=path;
    var provider=new SigText(md);
    foreach(var th in md.TypeDefinitions){var t=md.GetTypeDefinition(th);string name=FullName(md,th);var members=new Dictionary<string,List<string>>(StringComparer.Ordinal);
        foreach(var mh in t.GetMethods()){var m=md.GetMethodDefinition(mh);string n=md.GetString(m.Name);Add(members,n,Sig(m.DecodeSignature(provider,null)));}
        foreach(var fh in t.GetFields()){var f=md.GetFieldDefinition(fh);Add(members,md.GetString(f.Name),"field "+f.DecodeSignature(provider,null));}
        string baseType=t.BaseType.IsNil?"":provider.Handle(md,t.BaseType);
        targetTypes[asm+"|"+name]=(members,baseType);}
}
static void Add(Dictionary<string,List<string>> d,string k,string v){if(!d.TryGetValue(k,out var l))d[k]=l=[];l.Add(v);}
static string Sig(MethodSignature<string> s)=>$"{s.ReturnType}({string.Join(",",s.ParameterTypes)})"+(s.GenericParameterCount>0?"`"+s.GenericParameterCount:"");
static string FullName(MetadataReader md,TypeDefinitionHandle h){var t=md.GetTypeDefinition(h);string n=md.GetString(t.Name),ns=md.GetString(t.Namespace);
    if(t.GetDeclaringType() is {IsNil:false} outer)return FullName(md,outer)+"/"+n;return ns.Length>0?ns+"."+n:n;}

var results=new List<object>();int missing=0,checkedRefs=0;
var modNames=new HashSet<string>(args.Skip(2).Select(p=>{using var pe=new PEReader(File.OpenRead(p));var m=pe.GetMetadataReader();return m.GetString(m.GetAssemblyDefinition().Name);}),StringComparer.OrdinalIgnoreCase);
static bool IsFramework(string name)=>name is "mscorlib" or "netstandard"||name.StartsWith("System.",StringComparison.Ordinal)||name=="System"||name.StartsWith("Microsoft.CSharp",StringComparison.Ordinal)||name.StartsWith("Microsoft.Win32.",StringComparison.Ordinal)||name.StartsWith("Microsoft.VisualBasic",StringComparison.Ordinal)
    ||File.Exists(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),name+".dll"));
foreach(var modPath in args.Skip(2)){
    using var pe=new PEReader(File.OpenRead(modPath));var md=pe.GetMetadataReader();var provider=new SigText(md);
    string AsmOf(EntityHandle scope){
        if(scope.Kind==HandleKind.AssemblyReference)return md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
        if(scope.Kind==HandleKind.TypeReference)return AsmOf(md.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope);return "";}
    string RefName(TypeReferenceHandle h){var r=md.GetTypeReference(h);string n=md.GetString(r.Name),ns=md.GetString(r.Namespace);
        if(r.ResolutionScope.Kind==HandleKind.TypeReference)return RefName((TypeReferenceHandle)r.ResolutionScope)+"/"+n;return ns.Length>0?ns+"."+n:n;}
    var problems=new List<string>();
    foreach(var th in md.TypeReferences){string asm=AsmOf(th);if(!engineNames.Contains(asm))continue;checkedRefs++;
        if(!targetTypes.ContainsKey(asm+"|"+RefName(th))){problems.Add($"type {asm}:{RefName(th)}");missing++;}}
    foreach(var mh in md.MemberReferences){var m=md.GetMemberReference(mh);if(m.Parent.Kind!=HandleKind.TypeReference)continue;var parent=(TypeReferenceHandle)m.Parent;string asm=AsmOf(parent);if(!engineNames.Contains(asm))continue;
        checkedRefs++;string name=md.GetString(m.Name);string key=asm+"|"+RefName(parent);
        if(!targetTypes.TryGetValue(key,out var type)){continue;} // the type itself is already reported
        string sig=m.GetKind()==MemberReferenceKind.Method?Sig(m.DecodeMethodSignature(provider,null)):"field "+m.DecodeFieldSignature(provider,null);
        // Members may be inherited: walk the target base chain inside the engine assemblies.
        bool found=false;string cursor=key;int guard=0;
        while(cursor is not null&&guard++<20&&targetTypes.TryGetValue(cursor,out var t)){
            if(t.Members.TryGetValue(name,out var sigs)&&sigs.Contains(sig)){found=true;break;}
            cursor=t.BaseType.Length>0?t.BaseType:null;}
        if(!found){var alt=type.Members.TryGetValue(name,out var near)?string.Join(" | ",near):"no member of that name";problems.Add($"member {RefName(parent)}::{name} {sig} -> target has: {alt}");missing++;}}
    // Assemblies the mod references: each must exist in the target build (by name), or be one of the mods under test,
    // or come from the shared framework; anything else is reported (it would fail to load at run time).
    var assemblyRefs=new List<object>();
    foreach(var ah in md.AssemblyReferences){var a=md.GetAssemblyReference(ah);string name=md.GetString(a.Name);
        string where=targetAssemblies.ContainsKey(name)?"target":modNames.Contains(name)?"mod under test":IsFramework(name)?"framework":"MISSING";
        if(where=="MISSING"){problems.Add($"assembly {name} {a.Version} not in the target build");missing++;}
        assemblyRefs.Add(new{name,version=a.Version.ToString(),where});}
    results.Add(new{mod=Path.GetFileName(modPath),sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(modPath))).ToLowerInvariant(),assemblyRefs,problems});
}
File.WriteAllText(report,JsonSerializer.Serialize(new{targetFolder=folder,targetAssemblies,checkedReferences=checkedRefs,unresolved=missing,results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"MpAbiCheck: {checkedRefs} engine references checked, {missing} unresolved");return missing==0?0:1;

/// <summary>Signature types as comparable text (assembly-qualified for engine types).</summary>
sealed class SigText(MetadataReader md):ISignatureTypeProvider<string,object>{
    public string Handle(MetadataReader r,EntityHandle h)=>h.Kind switch{HandleKind.TypeDefinition=>GetTypeFromDefinition(r,(TypeDefinitionHandle)h,0),HandleKind.TypeReference=>GetTypeFromReference(r,(TypeReferenceHandle)h,0),_=>"?"};
    static string Asm(MetadataReader r,EntityHandle scope)=>scope.Kind==HandleKind.AssemblyReference?r.GetString(r.GetAssemblyReference((AssemblyReferenceHandle)scope).Name):scope.Kind==HandleKind.TypeReference?Asm(r,r.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope):"";
    public string GetTypeFromDefinition(MetadataReader r,TypeDefinitionHandle h,byte k){var t=r.GetTypeDefinition(h);string n=r.GetString(t.Name),ns=r.GetString(t.Namespace);
        string inner=t.GetDeclaringType() is {IsNil:false} o?GetTypeFromDefinition(r,o,k)+"/"+n:(ns.Length>0?ns+"."+n:n);return inner.Contains('|')?inner:r.GetString(r.GetAssemblyDefinition().Name)+"|"+inner;}
    public string GetTypeFromReference(MetadataReader r,TypeReferenceHandle h,byte k){var t=r.GetTypeReference(h);string n=r.GetString(t.Name),ns=r.GetString(t.Namespace);
        if(t.ResolutionScope.Kind==HandleKind.TypeReference)return GetTypeFromReference(r,(TypeReferenceHandle)t.ResolutionScope,k)+"/"+n;string a=Asm(r,t.ResolutionScope);
        a=a is "System.Runtime" or "System.Private.CoreLib" or "mscorlib" or "netstandard" or "System.Collections" or "System.Numerics.Vectors"?"bcl":a;return a+"|"+(ns.Length>0?ns+"."+n:n);}
    public string GetTypeFromSpecification(MetadataReader r,object c,TypeSpecificationHandle h,byte k)=>r.GetTypeSpecification(h).DecodeSignature(this,c);
    public string GetPrimitiveType(PrimitiveTypeCode t)=>t.ToString();
    public string GetSZArrayType(string e)=>e+"[]";public string GetArrayType(string e,ArrayShape s)=>e+"["+s.Rank+"]";
    public string GetByReferenceType(string e)=>e+"&";public string GetPointerType(string e)=>e+"*";
    public string GetGenericInstantiation(string g,ImmutableArray<string> a)=>g+"<"+string.Join(",",a)+">";
    public string GetGenericTypeParameter(object c,int i)=>"!"+i;public string GetGenericMethodParameter(object c,int i)=>"!!"+i;
    public string GetFunctionPointerType(MethodSignature<string> s)=>"fnptr";public string GetModifiedType(string m,string u,bool r)=>u;public string GetPinnedType(string e)=>e;
}
