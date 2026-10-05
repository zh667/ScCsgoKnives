using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Game;

public enum ScTravelCode {
    Ok, Repeat, SameWorld, NothingCarried,
    BadEnvelope, UnsupportedVersion, UnsupportedData, Corrupt, TransferReused,
    RegistryUnavailable, Busy, PendingObligations, Stacked, DuplicateCarried, MissingRecord,
    GrowthConflict, HeldElsewhere, ModelConflict, RegistryFull, SlotMismatch
}

/// <summary>One carried gun: its identity across worlds, the number it had in the world it left, and its whole record.</summary>
public sealed record ScTravelGun(string Identity, int Id, int Variant, string Row);
/// <summary>One carried slot holding a CS gun item (an instance, or a fresh gun: Identity empty).</summary>
public sealed record ScTravelSlot(int Slot, int Value, int Count, string Identity);

/// <summary>What a provider carries for one traveller on one trip: opaque to it, versioned, self-checking.</summary>
public sealed class ScTravelEnvelope {
    public const int CurrentVersion = 1;
    public const string Namespace = "zh667.ScCsgoKnives/guns";
    public int V { get; set; } = CurrentVersion;
    public string Ns { get; set; } = Namespace;
    /// <summary>The id of this transfer, made at export (one traveller, one departure). The same id again is the same
    /// transfer; a provider never has to invent or reuse one.</summary>
    public string Transfer { get; set; } = "";
    /// <summary>The stable identity of the world left (not its folder name).</summary>
    public string World { get; set; } = "";
    public string Traveller { get; set; } = "";
    public int Schema { get; set; }
    public int Layout { get; set; }
    public string Growth { get; set; } = "Unset";
    /// <summary>The gun block's index in the world left; the destination's may differ.</summary>
    public int Block { get; set; }
    public List<ScTravelGun> Guns { get; set; } = [];
    public List<ScTravelSlot> Slots { get; set; } = [];
    public string Digest { get; set; } = "";

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public string ComputeDigest() {
        var text = new StringBuilder();
        text.Append(V).Append('\n').Append(Ns).Append('\n').Append(Transfer).Append('\n').Append(World).Append('\n').Append(Traveller).Append('\n')
            .Append(Schema).Append('\n').Append(Layout).Append('\n').Append(Growth).Append('\n').Append(Block).Append('\n');
        foreach (var g in Guns.OrderBy(g => g.Identity, StringComparer.Ordinal)) text.Append(g.Identity).Append('|').Append(g.Id).Append('|').Append(g.Variant).Append('|').Append(g.Row).Append('\n');
        foreach (var s in Slots.OrderBy(s => s.Slot)) text.Append(s.Slot).Append('|').Append(s.Value).Append('|').Append(s.Count).Append('|').Append(s.Identity).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
    public string Encode() { Digest = ComputeDigest(); return JsonSerializer.Serialize(this, Json); }
    /// <summary>Reads an envelope. A text of a later version, of another namespace, or one that does not match its own
    /// digest is refused whole: nothing of it is applied and the provider keeps it as it is.</summary>
    public static ScTravelEnvelope Decode(string text, out ScTravelCode code, out string detail) {
        code = ScTravelCode.BadEnvelope; detail = "";
        if (string.IsNullOrWhiteSpace(text)) { detail = "没有携带枪械迁移数据"; return null; }
        ScTravelEnvelope e;
        try {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("v", out var version) || !version.TryGetInt32(out int v)) { detail = "迁移数据缺少版本"; return null; }
            if (v != CurrentVersion) { code = ScTravelCode.UnsupportedVersion; detail = $"迁移数据版本 {v}，本版只认识 {CurrentVersion}；数据原样保留，未应用"; return null; }
            e = JsonSerializer.Deserialize<ScTravelEnvelope>(text, Json);
        }
        catch (JsonException x) { detail = "迁移数据无法解析：" + x.Message; return null; }
        if (e is null || e.Ns != Namespace) { detail = "迁移数据不属于 CS 武器"; return null; }
        if (string.IsNullOrWhiteSpace(e.Transfer) || string.IsNullOrWhiteSpace(e.World) || e.Guns is null || e.Slots is null) { detail = "迁移数据缺少旅程或来源世界身份"; return null; }
        if (!string.Equals(e.Digest, e.ComputeDigest(), StringComparison.Ordinal)) { code = ScTravelCode.Corrupt; detail = "迁移数据与其校验不符（被截断或改动），未应用"; return null; }
        code = ScTravelCode.Ok; return e;
    }
}

/// <summary>The outcome of an export or an import.</summary>
public sealed record ScTravelResult(ScTravelCode Code, string Message, string Envelope = null, IReadOnlyDictionary<int, int> Values = null, int Guns = 0) {
    public bool Ok => Code is ScTravelCode.Ok or ScTravelCode.Repeat or ScTravelCode.SameWorld or ScTravelCode.NothingCarried;
}
