using DungeonCrawler.Core;

namespace DungeonCrawler.Adventure.Fortune;

/// <summary>Forty lines for a secret room's plaque when the floor carries no campaign clue. Stock text: it never names a shipped campaign.</summary>
public static class Epigraphs
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "The water went down. Nothing that drank it did.",
        "Count the steps on the way in. You will not be counting on the way out.",
        "Here lies a mason. He built the door he could not find.",
        "The lamps were lit for someone. It was not you.",
        "Below this stone, the cistern. Below the cistern, the reason.",
        "Whatever you hear in the pipes is also listening.",
        "Three brothers dug this. Two of them are the walls.",
        "The first toll is paid in the coin you carry. The second is paid in the coin you are.",
        "This chamber was sealed by order. The order was not signed.",
        "The drowned do not rot. They wait, which is worse.",
        "A ladder stood here. We do not speak of what climbed it.",
        "\"Turn back\" is written on the other side of this slab.",
        "Every god has plumbing. This one has guests.",
        "The rite required nine. The ninth required persuading.",
        "Keep to the dry side of the mark. The mark has moved.",
        "Here the surveyors stopped surveying.",
        "If the altar is warm, you are not the first today.",
        "The cold here is not weather. It is attention.",
        "The carver stopped mid-sentence. The sentence is still here.",
        "No one who reads this is lost. Lost people do not stop to read.",
        "The flood took the town and left the stairs. Draw your own conclusion.",
        "Beneath you, something is being kept. Above you, something is being kept out.",
        "A tally of the fallen was begun here. It ran out of wall.",
        "The vaults were emptied in a night. It was not a short night.",
        "Walk softly. The floor remembers weight.",
        "Fire was tried. The stone liked it.",
        "The last warden left his keys. He did not leave by the door.",
        "This far the light. No farther the lantern.",
        "It is not a maze. A maze wants you to leave.",
        "The well was dug for water. It struck something that was already drinking.",
        "Pray here if you must. Keep your voice down.",
        "Gold found below the waterline belongs to the water. Collect it anyway.",
        "The bells were taken down when they began ringing on their own.",
        "What the mouth swallows, the belly argues with.",
        "The children were told the lower stair was a story. The story was told the same about the children.",
        "Here a merchant set up shop. Business was slow, then sudden.",
        "The seal is intact. That was never the problem.",
        "Count the doors. Count them again. Report any difference to no one.",
        "The engineers called it the overflow. The priests called it the throat.",
        "Nothing sleeps down here. It only closes its eyes to be polite.",
    };

    /// <summary>A stable pick per game, floor and secret.</summary>
    public static string For(ulong gameSeed, int floor, int secretIndex)
        => All[(int)(Rng.Mix(gameSeed ^ (ulong)floor * 131 ^ (ulong)secretIndex) % (ulong)All.Count)];
}
