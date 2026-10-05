using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Engine;
namespace Game;

// current-direction-20260929 §6 M0, test-only: installed only in an isolated copy of the 1.9.3.1 game (never in the
// player's Mods). Mutes the game, waits until loading has finished and the menu has run for a while, writes what the
// original engine actually loaded (ScM0-standalone.json next to the exe) and exits.
public sealed class ScM0Harness131 : ModLoader {
    public override void __ModInitialize() {
        Mute();
        ModsManager.RegisterHook("OnLoadingFinished", this);
        Log.Information("[ScM0Harness] initialized");
    }

    static void Mute() {
        try { SettingsManager.MusicVolume = 0; SettingsManager.SoundsVolume = 0; } catch { }
    }

    public override void OnLoadingFinished(List<Action> actions) => actions.Add(() => {
        Mute();
        new Thread(Report) { IsBackground = true }.Start();
    });

    static void Report() {
        Thread.Sleep(12000);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        Type probe = assemblies.FirstOrDefault(a => a.GetName().Name == "ScCsgoNetProbe")?.GetType("Game.ScNetProbe", false);
        object Get(string property) => probe?.GetProperty(property)?.GetValue(null);
        var report = new {
            engine = "1.9.3.1 (isolated copy)",
            screen = ScreensManager.CurrentScreen?.GetType().Name,
            probeLoaded = probe is not null,
            probeEngine = Get("Engine")?.ToString(),
            decision = Get("Decision")?.ToString(),
            bridgeAttached = Get("Bridge") is not null,
            adapterAssemblyLoaded = assemblies.Any(a => a.GetName().Name == "ScCsgoNet"),
            multiplayerTypes = new[] { "Game.NetworkManager, Survivalcraft", "Game.ICompatNetAdapter, Survivalcraft.CompatNet" }.ToDictionary(n => n, n => Type.GetType(n, false) is not null),
            mods = ModsManager.ModList.Select(m => $"{m.modInfo?.PackageName} {m.modInfo?.Version}").ToArray(),
            modAssemblies = assemblies.Where(a => a.IsDynamic || string.IsNullOrEmpty(a.Location)).Select(a => a.GetName().Name).OrderBy(n => n).ToArray(),
        };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ScM0-standalone.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Log.Information("[ScM0Harness] report written; exiting");
        Thread.Sleep(1000);
        Environment.Exit(0);
    }
}
