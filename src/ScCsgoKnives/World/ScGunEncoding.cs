namespace Game;

/// <summary>Layout 6 keeps every valid v5 item byte-identical. Extended records use the
/// positive 17-bit data range; their model is read from the record, never guessed from a token.
/// The engine's sign bit (data bit 17) is not used. ID 1023 stays the empty template forever.</summary>
public static class ScGunEncoding {
    public const int PreviousLayout = 5, Layout = 6;
    public const int Tag = 1 << 16, PayloadMask = Tag - 1;
    public const int FirstExtendedId = 1024, LastId = FirstExtendedId + PayloadMask;
    public static bool Extended(int data) => (data & Tag) != 0;
    public static bool ValidBits(int data) => data >= 0 && data < (Tag << 1);
    public static bool IsRecordId(int id) => id >= 1 && id <= LastId && id != 1023;
    public static int NextId(int id) => id == 1023 ? FirstExtendedId : id;
    public static int Id(int data) => Extended(data) ? FirstExtendedId + (data & PayloadMask) : (data >> 6) & 1023;
    public static int Encode(int variant, int id) {
        if (variant < 0 || variant > 63 || id < 0 || id > LastId) throw new ArgumentOutOfRangeException();
        return id >= FirstExtendedId ? Tag | (id - FirstExtendedId) : variant | (id << 6);
    }
    public static bool Foreign(int data, ScGunRegistry registry) => !ValidBits(data)
        || (registry?.Disabled ?? false) || Extended(data) && (!(registry?.ExtendedEncodingEnabled ?? false)
            || !registry.TryGetSnapshot(Id(data), out _));
    public static int Variant(int data, ScGunRegistry registry) => !Extended(data) ? data & 63
        : registry is not null && registry.TryGetSnapshot(Id(data), out var state) ? state.Variant : -1;
    /// <summary>Detached save/travel validation uses the supplied registry and layout, never Current.</summary>
    public static bool Decode(int data, ScGunRegistry registry, int layout, out int id, out int variant) {
        id = -1; variant = -1;
        if (!ValidBits(data) || layout is not (PreviousLayout or Layout)) return false;
        if (Extended(data) && (layout != Layout || registry?.LoadedSchema != ScGunRegistry.Schema)) return false;
        id = Id(data); variant = Variant(data, registry);
        return variant >= 0 && variant < GunSpec.All.Length;
    }
}
