namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// What a hero calls on the rails: the cart warnings (a cart bearing down on someone on foot, a low beam, a hound about to
/// leap, a blocked line, a gap to jump, a barricade across the line, a live switch back early and at the last moment, a runaway wagon loose behind the train, the split behind it: wait, not yet, now) and the boarding. Three own lines by roster name (Sable's a woman's), two per class,
/// three generic; <see cref="TopUp"/> pours them into a campaign's bark set as <see cref="ReactiveBarks.TopUp"/> does, so
/// every campaign speaks them in each hero's voice. Stock only: the planner is never asked for them. Lines are the
/// deliverable and stay in this order. Engine-free.
/// </summary>
public static class RideBarks
{
    private sealed record Bank(string[] Brand, string[] Sable, string[] Wren, string[] Ilex, string[] Warrior, string[] Rogue, string[] Ranger, string[] Mage, string[] Generic);

    /// <summary>The six ride calls, the warning at a crevasse's edge on foot, the call of a barricade across the line ahead, a live switch back's two calls, the runaway behind, and the split's three calls.</summary>
    public static readonly BarkTrigger[] Triggers =
    {
        BarkTrigger.CartComing, BarkTrigger.AllAboard, BarkTrigger.Heads, BarkTrigger.HoundLeap, BarkTrigger.LineBlocked, BarkTrigger.JumpCall,
        BarkTrigger.Crevasse, BarkTrigger.Barricade, BarkTrigger.SwitchBack, BarkTrigger.SwitchBackNow, BarkTrigger.Runaway,
        BarkTrigger.SplitWait, BarkTrigger.SplitNotYet, BarkTrigger.SplitNow,
    };

    /// <summary>A set with fewer distinct lines than this for a ride trigger gets its whole tier from stock (a class-only member's two reach it too).</summary>
    public const int MinLines = 2;

    /// <summary>A hero's lines for a trigger: their own by roster name, else their class's, else the plain set; empty for a trigger this bank does not cover.</summary>
    public static IReadOnlyList<string> For(BarkTrigger t, string name, string classId)
    {
        if (!Banks.TryGetValue(t, out var b)) return Array.Empty<string>();
        switch (name.Trim().ToLowerInvariant())
        {
            case "brand": return b.Brand;
            case "sable": return b.Sable;
            case "wren": return b.Wren;
            case "ilex": return b.Ilex;
        }
        switch (classId.Trim().ToLowerInvariant())
        {
            case "class.warrior": return b.Warrior;
            case "class.rogue": return b.Rogue;
            case "class.ranger": return b.Ranger;
            case "class.mage": return b.Mage;
        }
        return b.Generic;
    }

    /// <summary>
    /// For every ride trigger with fewer than <see cref="MinLines"/> distinct lines, append the stock lines the set lacks
    /// (keyed by <see cref="BarkRepeat.Key"/>); returns how many were added.
    /// </summary>
    public static int TopUp(BarkSet set, string classId)
    {
        var added = 0;
        foreach (var t in Triggers)
        {
            var have = new HashSet<string>(set.Lines.Where(l => l.Trigger == t).Select(l => BarkRepeat.Key(l.Line.Text)));
            if (have.Count >= MinLines) continue;
            foreach (var text in For(t, set.Member, classId))
            {
                if (!have.Add(BarkRepeat.Key(text))) continue;
                set.Lines.Add(new BarkLine { Trigger = t, Line = new Line(text) });
                added++;
            }
        }
        return added;
    }

    private static readonly Dictionary<BarkTrigger, Bank> Banks = new()
    {
        [BarkTrigger.CartComing] = new Bank(
            Brand: new[] { "Cart! Off the rails!", "Into the wall, now!", "Cart coming. Move your feet." },
            Sable: new[] { "Cart! Get in a hole, darling!", "Off the line, off the line!", "Something heavy's coming. Move!" },
            Wren: new[] { "Cart. Pocket. Now.", "Off the rails.", "Wheels coming. Move." },
            Ilex: new[] { "A cart! Kindly vacate the rails!", "Into the niche! Momentum is not negotiable!", "Mass approaching at speed! Move!" },
            Warrior: new[] { "Cart! Move!", "Off the line!" },
            Rogue: new[] { "Cart! Hide!", "Off the rails, quick!" },
            Ranger: new[] { "Cart. Move.", "Pocket. Now." },
            Mage: new[] { "A cart! Clear the line!", "Into the wall!" },
            Generic: new[] { "Cart coming!", "Off the rails!", "Get in a pocket!" }),

        [BarkTrigger.AllAboard] = new Bank(
            Brand: new[] { "All aboard! Five seconds, then we roll.", "Get in or get walking.", "Seats, everyone. We're leaving." },
            Sable: new[] { "All aboard! Mind the splinters.", "In you get. I'm not waiting.", "Last call for the cart, loves." },
            Wren: new[] { "All aboard.", "In. We go.", "Seats. Now." },
            Ilex: new[] { "All aboard! I have claimed the dignified seat.", "Do hurry. The cart keeps a timetable.", "Boarding, everyone. Punctuality, please." },
            Warrior: new[] { "All aboard!", "Get in!" },
            Rogue: new[] { "All aboard, quick!", "In you get!" },
            Ranger: new[] { "In. Now.", "Aboard." },
            Mage: new[] { "All aboard, please.", "Board the cart!" },
            Generic: new[] { "All aboard!", "Get in the cart!", "We're leaving!" }),

        [BarkTrigger.Heads] = new Bank(
            Brand: new[] { "Heads!", "Duck!", "Beam! Heads down!" },
            Sable: new[] { "Heads, darlings!", "Duck! Mind the hair!", "Low beam! Down!" },
            Wren: new[] { "Heads.", "Down.", "Beam." },
            Ilex: new[] { "Heads! Beam ahead!", "Duck! Mind the timber!", "Lower your heads, all of you!" },
            Warrior: new[] { "Heads!", "Duck!" },
            Rogue: new[] { "Heads!", "Down!" },
            Ranger: new[] { "Heads.", "Down." },
            Mage: new[] { "Heads!", "Mind the beam!" },
            Generic: new[] { "Heads!", "Duck!", "Low beam!" }),

        [BarkTrigger.HoundLeap] = new Bank(
            Brand: new[] { "Hound! It's jumping!", "Dog on the cart!", "Hound coming over!" },
            Sable: new[] { "Hound! Not on my lap!", "Dog! Jumping!", "It's leaping! Swat it!" },
            Wren: new[] { "Hound.", "It'll jump.", "Hound. Watch it." },
            Ilex: new[] { "A hound, airborne! Repel it!", "It's leaping! Somebody hit it!", "Hound! I object to this!" },
            Warrior: new[] { "Hound!", "It's jumping!" },
            Rogue: new[] { "Hound! Jumping!", "Dog!" },
            Ranger: new[] { "Hound.", "Leaper." },
            Mage: new[] { "A hound!", "It's leaping!" },
            Generic: new[] { "Hound!", "It's jumping!", "Dog on the cart!" }),

        [BarkTrigger.LineBlocked] = new Bank(
            Brand: new[] { "Line's blocked! Take the switch!", "Pull the lever! Pull it!", "Heap ahead! Switch!" },
            Sable: new[] { "Line's blocked! Lever, quick!", "Switch it or we eat that heap!", "Pull something, anything!" },
            Wren: new[] { "Blocked. Switch.", "Lever. Now.", "Heap ahead. Switch." },
            Ilex: new[] { "The line is blocked! The switch!", "Throw the lever, unless we enjoy collisions!", "Obstruction ahead! Change tracks!" },
            Warrior: new[] { "Line's blocked! Switch!", "Pull the lever!" },
            Rogue: new[] { "Switch! Now!", "Blocked! Lever!" },
            Ranger: new[] { "Blocked. Switch.", "Lever." },
            Mage: new[] { "Blocked ahead! The switch!", "Change tracks!" },
            Generic: new[] { "Line's blocked, switch!", "Pull the lever!", "Switch tracks!" }),

        [BarkTrigger.JumpCall] = new Bank(
            Brand: new[] { "Pump it! We have to jump!", "Gap ahead! Push!", "Everyone pump! Now!" },
            Sable: new[] { "Pump! We're jumping, apparently!", "Faster, or we swim in that!", "Push, push, push!" },
            Wren: new[] { "Gap. Pump.", "Faster.", "Push. Now." },
            Ilex: new[] { "Pump! We need the velocity!", "A gap! Speed is our only argument!", "Push harder! I have done the sums!" },
            Warrior: new[] { "Pump it!", "Push!" },
            Rogue: new[] { "Pump! Faster!", "Push it!" },
            Ranger: new[] { "Pump.", "Faster." },
            Mage: new[] { "Pump! More speed!", "Faster, all of you!" },
            Generic: new[] { "Pump it, we have to jump!", "Faster!", "Push!" }),

        [BarkTrigger.Crevasse] = new Bank(
            Brand: new[] { "Mind the edge. That drop has no bottom.", "Stay off the lip. Nobody climbs out of that.", "Easy here. One wrong step and you're gone." },
            Sable: new[] { "Careful, darling. That hole goes all the way down.", "Mind your feet. I'm not fishing you out.", "Back from the edge, loves. It's hungry." },
            Wren: new[] { "Edge. Careful.", "Long drop. Don't.", "Stay back from that." },
            Ilex: new[] { "Mind the edge! I estimate it has no bottom.", "Do not lean over that. Gravity is unforgiving.", "A chasm! Single file, and look where you step!" },
            Warrior: new[] { "Mind the edge!", "Stay off the lip!" },
            Rogue: new[] { "Careful, big drop!", "Watch your feet!" },
            Ranger: new[] { "Edge. Careful.", "Long drop." },
            Mage: new[] { "Mind the chasm!", "Stay back from the edge!" },
            Generic: new[] { "Mind the edge!", "That drop has no bottom!", "Watch your step!" }),

        [BarkTrigger.Barricade] = new Bank(
            Brand: new[] { "Timber on the line! Cut it down!", "Barricade ahead! Hack it or brace!", "Planks across the track! Chop them!" },
            Sable: new[] { "Barricade, darling! Cut it or kiss it!", "Planks on the line! Somebody chop!", "Timber ahead, loves. Hack it down!" },
            Wren: new[] { "Barricade. Cut it.", "Timber ahead. Chop.", "Planks. Hit them." },
            Ilex: new[] { "A barricade! Hew it before we meet it!", "Timber across the rails! Cut it, or brace!", "Obstruction ahead! A spell would do nicely!" },
            Warrior: new[] { "Barricade! Cut it!", "Timber ahead! Chop!" },
            Rogue: new[] { "Planks ahead! Hack them!", "Barricade! Quick, cut it!" },
            Ranger: new[] { "Barricade. Cut.", "Timber. Chop." },
            Mage: new[] { "A barricade! Burn it or cut it!", "Timber ahead! Clear it!" },
            Generic: new[] { "Timber across the line — cut it!", "Barricade ahead!", "Cut it down!" }),

        [BarkTrigger.SwitchBack] = new Bank(
            Brand: new[] { "The bridge is out ahead! Throw the switch back!", "This line ends in the drop! Switch us back!", "Lever coming up! Back to our track!" },
            Sable: new[] { "That bridge is broken, darling! Switch us back!", "Lever, loves! Unless we fancy flying!", "Back onto our line! The bridge is gone!" },
            Wren: new[] { "Bridge out. Switch back.", "Lever ahead. Pull it.", "Back to our track." },
            Ilex: new[] { "The bridge ahead is broken! Throw the lever back!", "This line ends in a chasm! The switch, please!", "Return us to our track! Physics will not!" },
            Warrior: new[] { "Bridge is out! Switch back!", "Pull the lever back!" },
            Rogue: new[] { "Broken bridge! Switch us back!", "Lever! Quick, back over!" },
            Ranger: new[] { "Bridge out. Switch.", "Lever. Back over." },
            Mage: new[] { "The bridge is broken! The lever!", "Switch us back, now!" },
            Generic: new[] { "The slag line's out past the bridge — switch back!", "Bridge out ahead!", "Throw the switch back!" }),

        [BarkTrigger.SwitchBackNow] = new Bank(
            Brand: new[] { "Now! Pull it now!", "The lever! Now!", "Switch back, now!" },
            Sable: new[] { "Now, darling, NOW!", "Pull it! Pull it now!", "The lever, love! Now!" },
            Wren: new[] { "Now.", "Pull. Now.", "Lever. Now." },
            Ilex: new[] { "Now! The lever, now!", "Pull it this instant!", "Now, or never at all!" },
            Warrior: new[] { "Now! Pull it!", "Lever, now!" },
            Rogue: new[] { "Now! Pull it!", "Lever! Now!" },
            Ranger: new[] { "Now.", "Pull it." },
            Mage: new[] { "Now! The lever!", "Pull it, now!" },
            Generic: new[] { "Switch back — now!", "Now! The lever!", "Pull it now!" }),

        [BarkTrigger.Runaway] = new Bank(
            Brand: new[] { "Wagon loose behind us! Pump!", "Runaway on our tail! Put your backs in!", "Something's coming down the line! Pump!" },
            Sable: new[] { "Runaway behind us, darling! Pump!", "That wagon wants a kiss, loves. Pump!", "Loose wagon on our tail! Faster!" },
            Wren: new[] { "Wagon behind. Pump.", "Runaway. Faster.", "Behind us. Pump." },
            Ilex: new[] { "A runaway wagon behind us! Pump, all of you!", "It gains on us! More speed!", "Loose ore wagon astern! Pump or be rammed!" },
            Warrior: new[] { "Runaway behind! Pump!", "Wagon on our tail!" },
            Rogue: new[] { "Loose wagon behind! Pump!", "It's catching us! Faster!" },
            Ranger: new[] { "Wagon behind. Pump.", "Runaway. Faster." },
            Mage: new[] { "A runaway behind us! Pump!", "It gains on us! Faster!" },
            Generic: new[] { "Runaway on the line behind us — pump!", "Wagon loose behind us!", "Pump, it's gaining!" }),

        [BarkTrigger.SplitWait] = new Bank(
            Brand: new[] { "Let us pass the split first! Then throw it!", "Past the points, then throw it behind us!", "Wait till we're by the split, then pull!" },
            Sable: new[] { "Patience, darling! Pass the split, then throw it!", "Let us by first, loves, then pull!", "Let us pass the points, then pull!" },
            Wren: new[] { "Pass it first.", "Wait. Then throw.", "After the points." },
            Ilex: new[] { "We pass the split first, then throw it behind us!", "Let us clear the points, then pull it for the wagon!", "Let the points go by, then pull the lever!" },
            Warrior: new[] { "Pass the split, then throw it!", "Wait till we're by!" },
            Rogue: new[] { "Let us by first! Then pull!", "After the points! Then throw it!" },
            Ranger: new[] { "Pass it first.", "Wait. Then throw." },
            Mage: new[] { "Pass the split, then throw it!", "Wait until we're past the points!" },
            Generic: new[] { "Let us pass the split, then throw it!", "Wait till we pass it!", "Past the points first!" }),

        [BarkTrigger.SplitNotYet] = new Bank(
            Brand: new[] { "Not yet! Set it back, that's the spur!", "Back! You'll put us in the drop!", "Set it back! That's our death!" },
            Sable: new[] { "Not yet, darling! Set it back, that's the spur!", "Back, love! That way's the drop!", "Set it back! We're not flying today!" },
            Wren: new[] { "Not yet. Set it back.", "Back. That's the spur.", "Set it back." },
            Ilex: new[] { "Not yet! Set it back, that's the spur!", "Reverse it! That track ends in the chasm!", "Back, back! You'll send us into the drop!" },
            Warrior: new[] { "Not yet! Set it back!", "That's the spur! Back!" },
            Rogue: new[] { "Not yet! Back, back!", "That's the spur! Set it back!" },
            Ranger: new[] { "Not yet. Back.", "Spur. Set it back." },
            Mage: new[] { "Not yet! Set it back!", "That's the spur! Reverse it!" },
            Generic: new[] { "Not yet — set it back, that's the spur!", "Set it back!", "That's the spur!" }),

        [BarkTrigger.SplitNow] = new Bank(
            Brand: new[] { "Now! Throw the split behind us!", "We're past! Pull it now!", "Now! Send it down the spur!" },
            Sable: new[] { "Now, darling! Throw the split!", "We're by, love! Pull it now!", "Now! Let it have the drop!" },
            Wren: new[] { "Now. Throw it.", "Past. Pull.", "Now." },
            Ilex: new[] { "Now! Throw the split behind us!", "We are past the points! Pull it now!", "Now, and the wagon takes the spur!" },
            Warrior: new[] { "Now! Throw it!", "Past! Pull it now!" },
            Rogue: new[] { "Now! Throw it behind us!", "We're past! Pull!" },
            Ranger: new[] { "Now. Throw it.", "Past. Pull." },
            Mage: new[] { "Now! Throw the split!", "We're past! Pull it!" },
            Generic: new[] { "Now — throw the split!", "Throw it behind us!", "Pull it now!" }),
    };
}
