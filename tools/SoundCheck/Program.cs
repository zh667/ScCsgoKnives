using Engine.Media;
foreach (string path in Directory.GetFiles(args[0], "*.ogg").OrderBy(p => p)) {
    try {
        using FileStream fs = File.OpenRead(path);
        SoundData d = Ogg.Load(fs);
        Console.WriteLine($"OK   {Path.GetFileName(path),-36} ch={d.ChannelsCount} rate={d.SamplingFrequency} bytes={d.Data.Length}");
    }
    catch (Exception e) { Console.WriteLine($"FAIL {Path.GetFileName(path),-36} {e.GetType().Name}: {e.Message}"); }
}
