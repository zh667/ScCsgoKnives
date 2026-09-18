using Engine;
namespace Game;

/// <summary>Explicit, read-only disk inventory. Never runs in the per-frame/save path.</summary>
public static class ScWorldSizeReport {
    public static string Read(string directory) {
        var files=new List<(string Name,long Bytes)>();int folders=0;
        void Scan(string path,string prefix,int depth) {
            if(depth>24 || ++folders>10000 || files.Count>50000)throw new InvalidOperationException("文件过多，请在文件管理器中查看。");
            foreach(string name in Storage.ListFileNames(path)){
                if(files.Count>=50000)throw new InvalidOperationException("文件过多，请在文件管理器中查看。");
                files.Add((prefix+name,Storage.GetFileSize(Storage.CombinePaths(path,name))));
            }
            foreach(string child in Storage.ListDirectoryNames(path))Scan(Storage.CombinePaths(path,child),prefix+child+"/",depth+1);
        }
        Scan(directory,"",0);
        long backup=files.Where(p=>p.Name.EndsWith(".snapshot",StringComparison.OrdinalIgnoreCase)).Sum(p=>p.Bytes),total=files.Sum(p=>p.Bytes);
        string Size(long bytes)=>$"{bytes/1048576d:0.00} MiB";
        return $"世界目录大小：{Size(total)}\n其中备份：{Size(backup)}\n不含备份：{Size(total-backup)}\n文件数：{files.Count}\n\n最大的文件：\n"
            +string.Join("\n",files.OrderByDescending(p=>p.Bytes).Take(12).Select(p=>$"{p.Name}：{Size(p.Bytes)}"))
            +"\n\n只读统计，不删除任何文件；游戏正在运行时大小可能变化。请在退出保存后用文件管理器复核。不含备份也包括其他模组文件，不能直接视为地形大小。";
    }
}
