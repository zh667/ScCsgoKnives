namespace Game;

/// <summary>What a player's throw looks like right now (video-feedback-20260929 R2). Presentation only, produced by
/// the grenade subsystem from the same gameplay timeline that commits the throw, so every view - first person, the
/// vanilla body, a CS actor, another player's screen - agrees on the moment the grenade leaves the hand.
/// Value stays the thrown grenade until the action ends, also for the last one of a stack (whose slot is already
/// empty) and in creative mode (whose stack never shrinks): after <see cref="Released"/> the hand is empty.</summary>
/// <param name="Stage">0 pulling the pin / lighting, 1 holding ready, 2 throwing.</param>
/// <param name="Pull">0..1 through the pull; in the later stages where the pull ended (1, or less for a quick throw:
/// the button was let go before the pull was through, and the throw began there).</param>
/// <param name="Hold">Seconds held ready.</param>
/// <param name="Wind">0..1 from the start of the throw to the release.</param>
/// <param name="Follow">0..1 from the release to the end of the action.</param>
public readonly record struct ScThrowPhase(int Value,string Asset,int Stage,bool Low,bool Released,float Pull,float Hold,float Wind,float Follow) {
    public bool Active => Asset is not null;
}
