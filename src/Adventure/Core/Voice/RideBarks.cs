namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// What a hero calls on the rails: the cart warnings (a cart bearing down on someone on foot, a low beam, a hound about to
/// leap, a blocked line, a gap to jump, a barricade across the line, a live switch back early and at the last moment, a runaway wagon loose behind the train, the split behind it: wait, not yet, now; the cavern's bridge going behind the train, a brake on it, the slag line's drop, a boarder alone with the prisoners in a wagon, the lava pit's switch early and at the last moment) and the boarding. Three own lines by roster name (Sable's a woman's), two per class,
/// three generic; <see cref="TopUp"/> pours them into a campaign's bark set as <see cref="ReactiveBarks.TopUp"/> does, so
/// every campaign speaks them in each hero's voice. Stock only: the planner is never asked for them. Lines are the
/// deliverable and stay in this order. Engine-free.
/// </summary>
public static class RideBarks
{
    private sealed record Bank(string[] Brand, string[] Sable, string[] Wren, string[] Ilex, string[] Warrior, string[] Rogue, string[] Ranger, string[] Mage, string[] Generic);

    /// <summary>The six ride calls, the warning at a crevasse's edge on foot, the call of a barricade across the line ahead, a live switch back's two calls, the runaway behind, the split's three calls, the cavern's three (the bridge going, a brake on it, the slag line's drop), and a boarder alone with the prisoners (plan 14: never naming the wagon, the log does), and the lava pit's switch early and at the last moment (plan 15: never naming the switch's place).</summary>
    public static readonly BarkTrigger[] Triggers =
    {
        BarkTrigger.CartComing, BarkTrigger.AllAboard, BarkTrigger.Heads, BarkTrigger.HoundLeap, BarkTrigger.LineBlocked, BarkTrigger.JumpCall,
        BarkTrigger.Crevasse, BarkTrigger.Barricade, BarkTrigger.SwitchBack, BarkTrigger.SwitchBackNow, BarkTrigger.Runaway,
        BarkTrigger.SplitWait, BarkTrigger.SplitNotYet, BarkTrigger.SplitNow,
        BarkTrigger.BridgeGoing, BarkTrigger.BridgeBrake, BarkTrigger.SlagDrop,
        BarkTrigger.BoarderThrow,
        BarkTrigger.PitSwitch, BarkTrigger.PitSwitchNow,
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

        [BarkTrigger.BridgeGoing] = new Bank(
            Brand: new[] { "The bridge is going behind us! Keep rolling!", "It's falling in behind! Don't you stop!", "Bridge is coming down! Keep her moving!" },
            Sable: new[] { "The bridge is going, darling! Keep rolling!", "It's falling in behind us, love! Don't stop!", "Keep her moving, loves, the bridge is going!" },
            Wren: new[] { "Bridge going. Keep rolling.", "Behind us. Don't stop.", "It's falling. Keep moving." },
            Ilex: new[] { "The bridge collapses behind us! Keep rolling!", "It falls in at our backs! Do not stop!", "The timbers give way! Keep the cart moving!" },
            Warrior: new[] { "Bridge is going! Keep rolling!", "Don't stop, it's falling!" },
            Rogue: new[] { "Bridge is going behind us! Keep moving!", "It's falling in! Don't stop!" },
            Ranger: new[] { "Bridge going. Keep rolling.", "Don't stop." },
            Mage: new[] { "The bridge is collapsing! Keep rolling!", "It falls behind us! Don't stop!" },
            Generic: new[] { "The bridge is going behind us — keep her rolling!", "The bridge is falling in!", "Keep rolling, don't stop!" }),

        [BarkTrigger.BridgeBrake] = new Bank(
            Brand: new[] { "Off the brake! It's going under us!", "Don't stop here! Let her run!", "No brakes! The bridge is going!" },
            Sable: new[] { "Off the brake, darling, it's under us!", "Don't stop now, love! Let her run!", "No brakes, loves! The bridge is going!" },
            Wren: new[] { "Off the brake.", "Don't stop. Run.", "No brakes. Go." },
            Ilex: new[] { "Release the brake! It falls beneath us!", "Do not stop on this bridge!", "Off the brake, or we go down with it!" },
            Warrior: new[] { "Off the brake! It's going!", "Don't stop here!" },
            Rogue: new[] { "Off the brake! Run for it!", "Don't stop, it's under us!" },
            Ranger: new[] { "Off the brake.", "Don't stop." },
            Mage: new[] { "Release the brake! It's falling!", "Don't stop on the bridge!" },
            Generic: new[] { "Don't stop — it's going under us!", "Off the brake!", "Keep going, don't stop!" }),

        [BarkTrigger.SlagDrop] = new Bank(
            Brand: new[] { "The slag line's going! Leap back!", "Get off that cart! Jump back to us!", "Their deck's falling! Back here, now!" },
            Sable: new[] { "Their line's going, darling! Leap back!", "Off that cart, love! Jump back to us!", "Back here, loves, their deck is falling!" },
            Wren: new[] { "Slag line's going. Leap back.", "Jump back. Now.", "Their deck's falling. Back." },
            Ilex: new[] { "The slag line gives way! Leap back to us!", "Abandon that cart! Leap back across!", "Their deck falls! Back to our cart!" },
            Warrior: new[] { "Slag line's going! Leap back!", "Jump back to us!" },
            Rogue: new[] { "Their line's going! Leap back!", "Get off that cart! Jump!" },
            Ranger: new[] { "Leap back.", "Their deck's falling. Jump." },
            Mage: new[] { "The slag line falls! Leap back!", "Jump back across, now!" },
            Generic: new[] { "The slag line's going — leap back!", "Leap back to the cart!", "Jump back, now!" }),

        [BarkTrigger.BoarderThrow] = new Bank(
            Brand: new[] { "It's at the prisoners! Get back there!", "Hound in with the crew! Move!", "It'll throw them off! Get to them!" },
            Sable: new[] { "It's at the prisoners, darling! Go!", "Hound in with the crew, loves! Get back there!", "It'll toss them off, love! Somebody move!" },
            Wren: new[] { "Hound. With the crew.", "Prisoners. Get back there.", "It'll throw them. Go." },
            Ilex: new[] { "The hound is among the prisoners! Make haste!", "It means to hurl them off! Get back there!", "The prisoners are unguarded! Attend to them!" },
            Warrior: new[] { "It's at the prisoners!", "Get back to the crew!" },
            Rogue: new[] { "Hound on the crew! Quick!", "It's at the prisoners! Move!" },
            Ranger: new[] { "Crew. Hound.", "Get back there." },
            Mage: new[] { "The hound is at the prisoners!", "Get back to the crew!" },
            Generic: new[] { "It's at the prisoners!", "Get back to the crew!", "It'll throw them off!" }),

        [BarkTrigger.PitSwitch] = new Bank(
            Brand: new[] { "Lava on our line! Throw the switch!", "The track ends in fire! Switch us over!", "Pit of lava! Pull that lever!" },
            Sable: new[] { "Lava on the line, darling! The switch!", "That's a pit of fire, loves! Throw it!", "Switch us over, love, unless you like it hot!" },
            Wren: new[] { "Lava. Throw the switch.", "Pit on our line. Lever.", "Fire ahead. Switch over." },
            Ilex: new[] { "Molten rock on our line! Throw the switch!", "The rails end in lava! The lever, please!", "A pit of fire! Switch us round it!" },
            Warrior: new[] { "Lava pit! Throw the switch!", "Pull the lever!" },
            Rogue: new[] { "Lava on the line! Switch!", "Lever! Get us round it!" },
            Ranger: new[] { "Lava. Switch.", "Lever. Now." },
            Mage: new[] { "A lava pit! The lever!", "Switch us round the fire!" },
            Generic: new[] { "Lava pit on the line — throw the switch!", "Lava on our line!", "Throw the switch!" }),

        [BarkTrigger.PitSwitchNow] = new Bank(
            Brand: new[] { "Throw it! Now!", "The lever, now!", "Pull it now, or we burn!" },
            Sable: new[] { "Now, darling! Throw it!", "The lever, love, NOW!", "Pull it now, loves!" },
            Wren: new[] { "Throw it. Now.", "Lever. Now.", "Now." },
            Ilex: new[] { "Throw it this instant!", "The lever! Now, now!", "Now, or we are cinders!" },
            Warrior: new[] { "Throw it! Now!", "Lever, now!" },
            Rogue: new[] { "Now! Throw it!", "Pull it! Now!" },
            Ranger: new[] { "Now.", "Throw it." },
            Mage: new[] { "Now! The lever!", "Throw it, now!" },
            Generic: new[] { "Throw it — now!", "Now! The lever!", "Pull it now!" }),
    };
}
