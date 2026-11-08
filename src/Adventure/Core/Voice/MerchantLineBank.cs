namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// The stock merchant: a nervous, over-polite trader counting coins. Greedy and cheerful on floors 1-6, uneasy on
/// 7-13, frightened on 14-19, a send-off on 20. He never states any truth about the dungeon. Engine-free.
/// </summary>
public static class MerchantLineBank
{
    public static CastMember GenericCast() => new()
    {
        Id = "cast_merchant",
        Name = "The Merchant",
        Role = "a nervous trader who follows the party down to buy and sell",
        Secret = "",
        Merchant = true,
        Persona = new Persona { VoiceStyle = "a nervous, over-polite trader counting coins under his breath", DefaultDelivery = "calm", Voice = "Achird" },
    };

    /// <summary>What the hidden stall's keeper says (fortune): greeting, remark and trade line alike, by floor.</summary>
    public static readonly string[] HiddenGreeting = { "I was never here.", "You did not see a stall. You saw a wall with opinions.", "Everything here fell off a cart that does not exist.", "Quickly. The stones are listening, and they gossip." };

    /// <summary>The hidden stall's lines the deeds unlock (run-structure), by merchant-line id.</summary>
    public static readonly IReadOnlyDictionary<string, string> UnlockableLines = new Dictionary<string, string>
    {
        ["merchant.contract"] = "The whole contract, every clause, and still breathing. I keep a shelf for your sort. It was empty until now.",
        ["merchant.wainscot"] = "You found my other door. Nobody finds my other door. I shall have to move it, and the rent with it.",
        ["merchant.sineater"] = "You took the curse and kept the gold. Sensible. The gods do not audit; I do.",
        ["merchant.audience"] = "They say the guardian barely finished its sentence. I sell to the brisk. The brisk pay promptly.",
        ["merchant.regular"] = "Every day, down you come. Habit or hunger, I do not ask. I do stock accordingly.",
    };

    /// <summary>The unlocked line ids (run-structure); the profile's deeds fill it, and each one adds a greeting and a Rare roll to the hidden stall.</summary>
    public static HashSet<string> UnlockedLines { get; } = new();

    /// <summary>The hidden stall's greeting on a floor: the stock lines and the unlocked ones, by floor.</summary>
    public static string HiddenGreetingFor(int floor)
    {
        var pool = HiddenGreeting.Concat(UnlockableLines.Where(kv => UnlockedLines.Contains(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value)).ToList();
        return pool[Math.Abs(floor) % pool.Count];
    }

    public static MerchantPlan Generic(string campaignTitle, string antagonistName)
    {
        var plan = new MerchantPlan { Briefing = Briefing(campaignTitle, antagonistName) };
        var lines = Floors(campaignTitle, antagonistName);
        for (int i = 0; i < lines.Length; i++)
            plan.FloorLines.Add(new MerchantFloorLines
            {
                Floor = i + 1,
                Greeting = new Line(lines[i].g),
                Progress = new Line(lines[i].p),
                Trade = new Line(lines[i].t),
            });
        plan.Gathering.Add(new Line("Something is at the door. Do not mind it. I paid for a very good lock."));
        plan.Gathering.Add(new Line("More of them outside now. I can hear them breathing through the keyhole."));
        plan.Gathering.Add(new Line("The door is thick with them! Please, finish your business and go and be brave somewhere else."));
        return plan;
    }

    private static NpcScript Briefing(string title, string villain)
    {
        ChoicePlan C(string text, string next) => new() { Text = text, Next = next };
        DialogueNode N(string id, string text, params ChoicePlan[] cs) => new() { Id = id, Line = new Line(text), Choices = cs.ToList() };
        return new NpcScript
        {
            Start = "hire",
            Nodes =
            {
                N("hire", $"Good day, good day! A trader follows the brave, and you are very brave. I will meet you in the quiet rooms, with {title} in my ledger and wares in my pack.",
                    C("What do you sell?", "quakes"), C("Why follow us?", "quakes")),
                N("quakes", $"Prices are fair, mostly. Did you feel the floor just now? Nothing to worry about. {villain} is surely only moving furniture.",
                    C("Fair prices, then.", "deal"), C("Stay out of our way.", "deal")),
                N("deal", "Then we understand each other. You bring the gold, I bring the goods, and neither of us asks how deep it goes.",
                    C("Agreed.", "clauses"), C("We will see.", "clauses")),
                N("clauses", TavernRules.ClausesOffer,
                    C("Not today.", "sendoff"), new ChoicePlan { Text = "Read them.", Effect = DialogueEffect.Clauses, Next = "sendoff" }),
                N("sendoff", "Splendid. Do come back alive. Dead customers are dreadful for business, and worse for credit.",
                    new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell }),
            },
        };
    }

    private static (string g, string p, string t)[] Floors(string title, string villain) => new[]
    {
        ("Welcome, welcome! A shop with a door in the dark. Isn't it charming?", "First floor and not a scratch on you. Splendid for trade. Splendid.", "Everything priced to move, and I do love to see it move."),
        ("Ah, my favourite customers! My only customers, but still.", $"Word of {title} reaches even the cheap seats. Business will be brisk, I feel it.", "Coin on the counter, goods in your pack. The oldest courtesy there is."),
        ("There you are! I saved the good stock for you. Mostly.", "Third floor already. I told my wife you looked capable. I do not have a wife.", "A little less haggling and a little more buying, if it suits you."),
        ("Do come in, mind the lamp. It is the only one I could afford.", "You are doing marvellously. Each floor, another purse of gold. For you, I mean. And then me.", "Prices are firm, but my smile is free."),
        ("Back so soon? Pleasure, pleasure. I shall put the kettle on. There is no kettle.", "Five floors. My accountant says I should charge more. I am considering it.", "Take your time. Time is money, but I am not."),
        ("A merchant's life is a happy one, down here with the profits.", $"Floor six. They say {villain} dislikes visitors. Terrible manners, but excellent for sales.", "Buy now, before the market turns. Markets turn. Everything turns."),
        ("Ah, there you are. Was it this quiet last time? It was louder, I think.", "I thought I saw a tunnel that is not on my survey. I am sure it is nothing. My eyes are old.", "Do buy something. It steadies my hands."),
        ("Good evening. Or morning. I have lost count, the lamp makes everything a little the same.", "Did you feel that? It seemed to come at even intervals, like a clock. Silly, I know. My nerves.", "Prices have, ah, gone up a little. Shipping, you understand."),
        ("Oh! It is you. Thank goodness. I mean, welcome. Welcome!", "My investors keep writing to ask what I have seen. I keep writing back that I have seen nothing. That is true, mostly.", "Please buy quickly. I would like to be elsewhere, and I have no idea where."),
        ("Mind your step. And mind mine. I keep hearing it behind me.", "Ten floors. A round number. I always liked round numbers, they cannot hide anything in the corners.", "Shall we say the usual rates? I would hate to quarrel."),
        ("Come in, come in, shut the door. Properly. Thank you.", $"The quakes keep time now, and I have started to count along. Do you think {villain} can hear me counting?", "Gold, please. It is the one thing in this place that makes any sense."),
        ("I hoped it was you. Every footstep, I hoped it was you.", "My investors asked if the shop is safe. I wrote yes. My hand shook, so it looked like a no.", "I will not haggle today. Take what you need, and pay what you can."),
        ("Ah. Good. You are alive. That makes two of us.", "Thirteen floors. I am not superstitious, but I am keeping a very small list of exits.", "Business as ever, then. Business is calming. Do buy something calming."),
        ("Quickly, in, in. Do not stand in the doorway. It is drafty. It is only drafty.", "I thought I heard digging below. Probably pipes. I do not know who would dig.", "Take what you like. I would sooner be paid, but I would sooner be elsewhere still."),
        ("You came back. I told myself you would, and I did not believe me.", "Something below sounds closer every night. I may be wrong. I hope I am.", "The prices are what they are. I cannot think about prices. Please just pick."),
        ("Oh, thank you, thank you. I was talking to the crates.", $"If you meet {villain}, tell them I was never here. Tell them I sell only soap.", "I have the good stock. I was saving it for a better day. This seems to be the day."),
        ("Do not mind my hands. They have a mind of their own, and it is a frightened one.", "The wall by my cot is warm. It was cold yesterday. I have moved my cot.", "A fair price, a fair deal, and then, please, do go on. I mean that kindly."),
        ("Is that you? Say something, so that I know it is you.", $"{title} was meant to be a short job. A modest profit. Now I sleep with my boots on.", "Take it. Take it all, if you must. Only leave me the lamp."),
        ("There you are. There you are. I counted your steps all the way down.", "I keep thinking it is closer. I may be wrong. I hope I am. Please hurry.", "Pay what you like. Coin is a poor comfort down here, though I do keep counting it."),
        ("Last of the quiet rooms, I think. You have come further than anyone I ever sold to.", $"Whatever waits below, and whether {villain} is the whole of it, you have my thanks, and my very best discount.", "Everything must go, as they say. Do take it. Go well, and come back, and tell me everything."),
    };
}
