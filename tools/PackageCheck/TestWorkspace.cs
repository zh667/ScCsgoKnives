/// <summary>Each check owns an isolated temporary tree on the project drive. All fixture
/// GetTempPath calls inherit it; disposing removes only this run, never user/world folders.</summary>
sealed class TestWorkspace : IDisposable {
    readonly Dictionary<string,string> previous = new();
    public string DirectoryPath { get; }
    public TestWorkspace() {
        string configured = Environment.GetEnvironmentVariable("SC_CSGO_DEV_TEMP");
        string root = configured;
        if (string.IsNullOrWhiteSpace(root)) {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName,"src","ScCsgoKnives","ScCsgoKnives.csproj"))) directory = directory.Parent;
            root = Path.Combine(directory?.FullName ?? Environment.CurrentDirectory,".tmp","dev-temp");
        }
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        DirectoryPath = Path.Combine(root,"check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        foreach (string name in new[]{"TEMP","TMP","TMPDIR"}) {
            previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name,DirectoryPath);
        }
        Console.Error.WriteLine("PackageCheck temporary workspace: "+DirectoryPath);
    }
    public void Dispose() {
        foreach(var pair in previous) Environment.SetEnvironmentVariable(pair.Key,pair.Value);
        // DirectoryPath is the unique directory created above, not an input path or glob.
        // Skip cleanup on explicit request so failed fixtures can be inspected.
        if(Environment.GetEnvironmentVariable("SC_CSGO_KEEP_TEST_TEMP")=="1") {
            Console.Error.WriteLine("Keeping test workspace: "+DirectoryPath); return;
        }
        try { Directory.Delete(DirectoryPath,recursive:true); }
        catch(Exception e) { Console.Error.WriteLine("Test workspace cleanup deferred: "+DirectoryPath+"; "+e.Message); }
    }
}
