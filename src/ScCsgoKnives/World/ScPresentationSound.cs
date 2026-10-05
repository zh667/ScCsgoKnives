using System.Globalization;
using Engine.Audio;
namespace Game;

/// <summary>Visible first-person mechanics use the animation clock, not simulation-speed filtered world audio.
/// The mechanical sounds of what a player holds (draws, inspect and clip cues) belong to that player's held action
/// (post-mp-bugs-20260930 §4): a slot switch, leaving CS items, a cancelled draw/inspect or leaving the world fades the
/// older ones out over <see cref="FadeSeconds"/>, so quick scrolling never stacks deploy sounds that outlive the item. Files
/// always play at their own length and pitch until then. Other presentation sounds (a knife swing) and world sounds are
/// not owned and never stopped here.</summary>
public static class ScPresentationSound {
    /// <summary>Fade of a released held sound: short enough to end with the switch, long enough not to click.</summary>
    public const float FadeSeconds = .06f;
    /// <summary>Held sounds one owner keeps at once (fading ones included); the oldest stops at once beyond it.</summary>
    public const int MaxHeld = 4;
    sealed class Held {
        public object Owner; public long Action; public string Name; public Sound Sound; public float Gain;
        public double StartedAt, FadeFrom = -1; public int Id; public string Reason;
    }
    static readonly List<Held> s_held = [];
    static readonly Engine.Random s_random = new();
    static int s_ids;

    public static void Play(string name, float volume = 1, float pitch = 0) => Start(name, volume, pitch, null, -1);
    /// <summary>A mechanical sound of <paramref name="owner"/>'s (a first-person model's) action <paramref name="action"/>
    /// (KnifeAnimationController.ActionToken).</summary>
    public static void PlayHeld(object owner, long action, string name, float volume = 1, float pitch = 0) {
        if (owner is null) { Play(name, volume, pitch); return; }
        Start(name, volume, pitch, owner, action);
    }

    static void Start(string name, float volume, float pitch, object owner, long action) {
        if (!Engine.Window.IsActive) return;
        float gain = Math.Clamp(SettingsManager.SoundsVolume, 0, 1) * volume;
        if (gain <= AudioManager.MinAudibleVolume) return;
        if (Cs2SoundVariants.All.TryGetValue(name, out int count)) name += "_" + s_random.Int(1, count);
        try {
            var sound = new Sound(ContentManager.Get<SoundBuffer>("Audio/ScCsgoKnives/" + name), gain, MathF.Pow(2, pitch), 0, false, true);
            if (owner is not null) {
                int mine = 0;
                foreach (var h in s_held) if (ReferenceEquals(h.Owner, owner)) mine++;
                while (mine >= MaxHeld) { StopNow(Oldest(owner), "cap"); mine--; }
                var held = new Held { Owner = owner, Action = action, Name = name, Sound = sound, Gain = gain, StartedAt = Engine.Time.RealTime, Id = ++s_ids };
                s_held.Add(held);
                sound.Play();
                if (Record) Note($"start #{held.Id} {name} {Describe(owner)} action {action} held {Count(owner)}");
            }
            else { sound.Play(); if (Record) Note($"start (unowned) {name}"); }
        }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("presentation-sound/" + name, "Animation sound " + name + ": " + e.Message); }
    }

    /// <summary>Fades out <paramref name="owner"/>'s held sounds: all but those of <paramref name="keepAction"/>, or only those
    /// of <paramref name="onlyAction"/>. Returns how many began fading.</summary>
    public static int Release(object owner, string reason, long keepAction = long.MinValue, long onlyAction = long.MinValue) {
        if (owner is null || s_held.Count == 0) return 0;
        int n = 0; double now = Engine.Time.RealTime;
        foreach (var h in s_held) {
            if (!ReferenceEquals(h.Owner, owner) || h.FadeFrom >= 0 || h.Action == keepAction || onlyAction != long.MinValue && h.Action != onlyAction) continue;
            h.FadeFrom = now; h.Reason = reason; n++;
        }
        if (n > 0 && Record) Note($"release {n} {Describe(owner)} ({reason}) held {Count(owner)}");
        return n;
    }

    /// <summary>Leaving the world: every held sound stops at once.</summary>
    public static void ReleaseAll(string reason) {
        foreach (var h in s_held.ToArray()) StopNow(h, reason);
    }

    /// <summary>Once per frame (any screen): advances fades and forgets sounds that ended by themselves.</summary>
    public static void Tick() {
        if (s_held.Count == 0) return;
        double now = Engine.Time.RealTime;
        for (int i = s_held.Count - 1; i >= 0; i--) {
            var h = s_held[i];
            SoundState state;
            try { state = h.Sound.State; } catch { state = SoundState.Disposed; }
            if (state is SoundState.Stopped or SoundState.Disposed) {
                s_held.RemoveAt(i);
                if (Record) Note($"end #{h.Id} {h.Name} after {now - h.StartedAt:0.000}s{(h.FadeFrom >= 0 ? " (while fading)" : "")}");
                continue;
            }
            if (h.FadeFrom < 0) continue;
            double t = (now - h.FadeFrom) / FadeSeconds;
            if (t >= 1) { StopNow(h, h.Reason); continue; }
            try { h.Sound.Volume = h.Gain * (float)(1 - t); } catch { StopNow(h, h.Reason); }
        }
    }

    /// <summary>Held sounds of this owner still playing (fading ones included).</summary>
    public static int Count(object owner) { int n = 0; foreach (var h in s_held) if (ReferenceEquals(h.Owner, owner)) n++; return n; }

    /// <summary>The one to drop first: the oldest already fading, else the oldest.</summary>
    static Held Oldest(object owner) {
        Held fading = null, any = null;
        foreach (var h in s_held) {
            if (!ReferenceEquals(h.Owner, owner)) continue;
            if (any is null || h.Id < any.Id) any = h;
            if (h.FadeFrom >= 0 && (fading is null || h.Id < fading.Id)) fading = h;
        }
        return fading ?? any;
    }

    static void StopNow(Held h, string reason) {
        if (h is null) return;
        s_held.Remove(h);
        try { h.Sound.Stop(); } catch { }
        if (Record) Note($"stop #{h.Id} {h.Name} after {Engine.Time.RealTime - h.StartedAt:0.000}s ({reason}) held {Count(h.Owner)}");
    }

    static string Describe(object owner) =>
        owner is ComponentFirstPersonModel m && m.m_componentPlayer?.PlayerData is { } d
            ? $"P{d.PlayerIndex} slot {m.m_componentPlayer.ComponentMiner?.Inventory?.ActiveSlotIndex ?? -1}" : "owner";

    // ---------------------------------------------------------------- diagnostics (off unless a test switches it on)
    /// <summary>Test diagnostics: when on, every held-sound start/release/stop/end and every selection change is kept in
    /// <see cref="JournalText"/> (bounded) with the real-time clock and UTC, for runtime logs and recordings.</summary>
    public static bool Record;
    static readonly Queue<string> s_journal = new();
    public const int JournalLimit = 2000;
    public static void Note(string entry) {
        if (!Record) return;
        if (s_journal.Count >= JournalLimit) s_journal.Dequeue();
        s_journal.Enqueue(Engine.Time.RealTime.ToString("0.0000", CultureInfo.InvariantCulture) + " " + DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + entry);
    }
    public static string JournalText => string.Join("\n", s_journal);
    public static void ClearJournal() => s_journal.Clear();
}
