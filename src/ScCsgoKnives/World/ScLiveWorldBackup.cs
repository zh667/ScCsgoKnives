using System.Reflection;
using System.Threading;
namespace Game;

/// <summary>Native region streams are opened exclusively. Pause the terrain worker, take the
/// serializer's own IO lock, release its lazy-reopened REGION cache, then copy+verify all files.
/// Never dispose the serializer/updater/world or skip a locked region.</summary>
public static class ScLiveWorldBackup {
    static readonly FieldInfo StorageField=typeof(TerrainSerializer23).GetField("m_storage",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
    static readonly FieldInfo LockField=typeof(TerrainSerializer23).GetField("m_lock",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
    public static string Snapshot(SubsystemTerrain terrain,string directory,string name,int schema) =>
        WithReleasedRegions(terrain.TerrainSerializer,terrain.TerrainUpdater.UpdateEvent,
            ()=>ScGunSchemaUpgrade.Snapshot(directory,name,schema));
    public static string WithReleasedRegions(TerrainSerializer23 serializer,AutoResetEvent updateEvent,Func<string> backup) {
        if(serializer is null||updateEvent is null||backup is null||StorageField is null||LockField is null)
            throw new InvalidOperationException("无法确认地形存储接口，未开始备份或穿越");
        if(serializer.GetType()!=typeof(TerrainSerializer23))throw new InvalidOperationException("第三方地形序列化器尚未验证在线备份，请先退出世界备份");
        if(!updateEvent.WaitOne(5000))throw new InvalidOperationException("地形线程忙，请返回游戏稍等后重试");
        bool locked=false;object gate=null;
        try {
            gate=LockField.GetValue(serializer)??throw new InvalidOperationException("地形 IO 锁不可用");
            Monitor.TryEnter(gate,5000,ref locked);
            if(!locked)throw new InvalidOperationException("地形正在读写，请稍后重试");
            var storage=StorageField.GetValue(serializer);
            // Only the inspected native cache is safe to release. SingleFileStorage/third-party
            // storage can retain non-reopenable state and must NOT be disposed by this helper.
            if(storage?.GetType()!=typeof(TerrainSerializer23.RegionFileStorage))
                throw new InvalidOperationException("当前地形存储不支持在线备份，请先退出世界备份");
            // Native Dispose closes OpenedStreams only; GetRegionStream explicitly reopens
            // cached streams whose CanRead is false. m_storage itself remains installed.
            ((TerrainSerializer23.RegionFileStorage)storage).Dispose();
            return backup();
        }finally {
            if(locked)Monitor.Exit(gate);
            updateEvent.Set();
        }
    }
}
