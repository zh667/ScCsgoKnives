using System.Xml.Linq;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>1.7.0 (feedback-1124-170 D22-D28): the first time a player makes a gun model at the weapon workbench, one survivor
/// case. Per player (the same durable owner the defuse reward uses) and per world; every gun model counts once, guns made
/// before 1.7.0 included (the ledger starts empty, D23); knives, creative mode, a cancelled or failed assembly and any
/// other way of obtaining a gun never count. A full inventory drops the case at the player's feet, once (D25).</summary>
public sealed class ScFirstCraftLedger {
    public const int Schema = 1;
    readonly Dictionary<string, SortedSet<string>> m_made = new(StringComparer.Ordinal);
    /// <summary>A saved ledger of an unsupported schema, kept exactly as it was and never added to.</summary>
    public ValuesDictionary Unsupported { get; private set; }
    public bool Has(string owner, string gun) => m_made.TryGetValue(owner ?? "", out var set) && set.Contains(gun);
    public IReadOnlyCollection<string> Owners => m_made.Keys;
    /// <summary>Records this owner's first assembly of this gun model; false when it was already recorded (or cannot be).</summary>
    public bool TryMark(string owner, string gun) {
        if (Unsupported is not null || string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(gun) || owner.Length > 256) return false;
        if (!m_made.TryGetValue(owner, out var set)) m_made[owner] = set = new(StringComparer.Ordinal);
        return set.Add(gun);
    }
    public void Save(ValuesDictionary values) {
        if (Unsupported is not null) { foreach (var pair in Unsupported) values.SetValue(pair.Key, pair.Value); return; }
        values.SetValue("Schema", Schema);
        var players = new ValuesDictionary(); int i = 0;
        foreach (var (owner, guns) in m_made.OrderBy(p => p.Key, StringComparer.Ordinal)) {
            var entry = new ValuesDictionary(); entry.SetValue("Owner", owner); entry.SetValue("Guns", string.Join(";", guns));
            players.SetValue((i++).ToString(System.Globalization.CultureInfo.InvariantCulture), entry);
        }
        values.SetValue("Players", players);
    }
    /// <summary>Unknown gun names are kept as they are (a newer catalogue); a future schema is kept untouched and read-only.</summary>
    public static ScFirstCraftLedger Load(ValuesDictionary values) {
        var ledger = new ScFirstCraftLedger();
        if (values is null || values.Count == 0) return ledger;
        if (values.GetValue("Schema", Schema) != Schema) { ledger.Unsupported = Copy(values); return ledger; }
        foreach (var pair in values.GetValue<ValuesDictionary>("Players", new ValuesDictionary())) {
            if (pair.Value is not ValuesDictionary entry) continue;
            string owner = entry.GetValue("Owner", "");
            if (owner.Length == 0) continue;
            foreach (string gun in entry.GetValue("Guns", "").Split(';', StringSplitOptions.RemoveEmptyEntries)) ledger.TryMark(owner, gun);
        }
        return ledger;
    }
    static ValuesDictionary Copy(ValuesDictionary v) { var x = new XElement("Values"); v.Save(x); var r = new ValuesDictionary(); r.ApplyOverrides(x); return r; }

    /// <summary>The gun model a workbench result makes, when it is one that counts (a gun, outside creative mode); null otherwise.</summary>
    public static string CountedGun(ScWeaponCrafting.Entry entry, bool creative) => !creative && entry is { Knife: false } ? entry.Name : null;

    /// <summary>The one place an assembly turns into a reward: the craft runs first; only a success that marks the ledger grants.
    /// An exception or a refusal of <paramref name="craft"/> leaves the ledger untouched.</summary>
    public bool CraftAndReward(Func<bool> craft, string owner, ScWeaponCrafting.Entry entry, bool creative, Action<string> grant) {
        bool done = craft();
        if (done && CountedGun(entry, creative) is { } gun && TryMark(owner, gun)) grant(gun);
        return done;
    }
}

/// <summary>Owns this world's first-craft ledger (Project.xml group ScFirstCraft) and grants the case. The server alone
/// decides in multiplayer: both the local workbench commit and the server's commit for a remote client come here.</summary>
public sealed class SubsystemScFirstCraft : Subsystem {
    public const string GroupName = "ScFirstCraft";
    public ScFirstCraftLedger Ledger { get; private set; } = new();
    public override void Load(ValuesDictionary values) { base.Load(values); Ledger = ScFirstCraftLedger.Load(values); }
    public override void Save(ValuesDictionary values) { base.Save(values); Ledger.Save(values); }

    /// <summary>Runs <paramref name="craft"/> for this player's inventory and, on a first gun, grants one case.</summary>
    public static bool Craft(ComponentPlayer player, IInventory inventory, int craftedValue, bool creative, Func<bool> craft) {
        var project = player?.Project;
        var self = project?.FindSubsystem<SubsystemScFirstCraft>(false);
        if (self is null || !ScNet.IsAuthority) return craft();
        string owner = ScGunHolders.RecoveryOwner(project, inventory);
        return self.Ledger.CraftAndReward(craft, owner, creative ? null : ScWeaponCrafting.Find(craftedValue), creative, gun => Grant(player, inventory, gun));
    }
    static void Grant(ComponentPlayer player, IInventory inventory, string gun) {
        if (CaseAcquisition.Survivor is null) return;
        int value = CaseAcquisition.CaseValue();
        int remaining = ComponentInventoryBase.AcquireItems(inventory, value, 1);
        if (remaining > 0) player.Project.FindSubsystem<SubsystemPickables>(true).AddPickable(value, remaining, player.ComponentBody.Position + new Vector3(0, .5f, 0), null, null, player.Entity);
        int variant = Array.FindIndex(GunSpec.All, g => g.Name == gun);
        string text = string.Format(LanguageControl.TryGet(out string t, "ScCsgoBox", "Message", "FirstCraft") ? t : "首次制作 {0}：获得 1 个荒野武器箱。", ScGunNames.Variant(variant));
        if (ScNet.IsLocal(player)) player.ComponentGui.DisplaySmallMessage(text, Color.LightYellow, true, false); else ScNetFeedback.Tell(player, text, Color.LightYellow);
    }
}

/// <summary>The world-folder copy of the first-craft ledger (user decision 2026-11-24, D26; the same rule as
/// ScGunRecordMirror): every save writes the group to ScCsgoFirstCraft.xml (previous copy kept as .bak); a load reads the
/// copy only when Project.xml has no ScFirstCraft group (the world was saved without CS) and only when the copy's seed is
/// this world's. Project.xml stays the authority; the copy is never merged with a present group.</summary>
public static class ScFirstCraftMirror {
    public const string FileName = "ScCsgoFirstCraft.xml";
    const string RootName = "ScFirstCraftMirror";
    static XElement Group(XElement parent, string name) => parent?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == name);
    static string Text(XElement parent, string name) => (string)parent?.Elements("Value").SingleOrDefault(e => (string)e.Attribute("Name") == name)?.Attribute("Value");
    static string Seed(XElement project) => Text(Group(project.Element("Subsystems"), "GameInfo"), "WorldSeed");
    /// <summary>The copy of a saved project's ledger, or null when it has none (or no seed).</summary>
    public static XElement Document(XElement project) {
        var group = Group(project?.Element("Subsystems"), SubsystemScFirstCraft.GroupName); string seed = project is null ? null : Seed(project);
        return group is null || seed is null ? null : new XElement(RootName, new XAttribute("Seed", seed), new XAttribute("Build", ScCompatibility.ActiveBuild), new XElement(group));
    }
    /// <summary>Puts the copy's group back into a project that lost it. True when restored.</summary>
    public static bool Restore(XElement project, XElement copy) {
        var subs = project?.Element("Subsystems");
        if (subs is null || copy?.Name != RootName || Group(subs, SubsystemScFirstCraft.GroupName) is not null) return false;
        string seed = Seed(project);
        if (seed is null || seed != (string)copy.Attribute("Seed")) { KnifeLog.Warning("[FIRST_CRAFT] ledger copy belongs to another world (seed); not used"); return false; }
        var group = copy.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == SubsystemScFirstCraft.GroupName);
        if (group is null) return false;
        subs.Add(new XElement(group)); return true;
    }
    /// <summary>OnProjectXmlSaved (the saving thread). Never fails the save.</summary>
    public static void CaptureSaved(XElement project) {
        try {
            var document = Document(project); if (document is null) return;
            string world = Text(Group(project.Element("Subsystems"), "ScGunBlockBehavior"), ScGunTravel.SourcePath);
            if (string.IsNullOrWhiteSpace(world)) return;
            ScGunRecordMirror.Write(Storage.GetSystemPath(Storage.CombinePaths(world, FileName)), document);
        }
        catch (Exception e) { KnifeLog.Warning("[FIRST_CRAFT] ledger copy not written; the world save itself is unaffected: " + e.Message); }
    }
    /// <summary>ProjectXmlLoad: restores a lost ledger group from the world folder's copy (or its .bak).</summary>
    public static bool BeforeLoad(XElement project, WorldInfo world) {
        try {
            if (string.IsNullOrWhiteSpace(world?.DirectoryName) || Group(project?.Element("Subsystems"), SubsystemScFirstCraft.GroupName) is not null) return false;
            string path = Storage.GetSystemPath(Storage.CombinePaths(world.DirectoryName, FileName));
            foreach (string candidate in new[] { path, path + ".bak" }) {
                if (!System.IO.File.Exists(candidate)) continue;
                try { if (Restore(project, XElement.Load(candidate))) { KnifeLog.Warning("[FIRST_CRAFT] ledger restored from " + System.IO.Path.GetFileName(candidate)); return true; } }
                catch (Exception e) { KnifeLog.Warning("[FIRST_CRAFT] " + System.IO.Path.GetFileName(candidate) + " unreadable: " + e.Message); }
            }
            return false;
        }
        catch (Exception e) { KnifeLog.Warning("[FIRST_CRAFT] ledger restore check failed: " + e.Message); return false; }
    }
}
