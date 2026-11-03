namespace DungeonCrawler.Net;

/// <summary>
/// The hit effects the server relays to its clients in a <see cref="HitFxEvt"/>: blood from a melee blow and an arrow left
/// stuck in an enemy, both produced only where the hit is resolved. No spell burst on purpose: every machine bursts its
/// own copy of the bolt where it lands, so relaying one would draw it twice.
/// </summary>
public enum HitFxKind : byte { Blood = 1, Arrow = 2 }
