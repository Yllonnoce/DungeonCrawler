namespace DungeonCrawler.Net;

/// <summary>A position or velocity without the engine's vector type, so it compiles in the test project.</summary>
public readonly record struct NetVec(float X, float Y, float Z)
{
    public static readonly NetVec Zero = new(0, 0, 0);
    public static NetVec operator +(NetVec a, NetVec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static NetVec operator -(NetVec a, NetVec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static NetVec operator *(NetVec a, float s) => new(a.X * s, a.Y * s, a.Z * s);
    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);
    public static NetVec Lerp(NetVec a, NetVec b, float t) => a + (b - a) * t;
}

[Flags]
public enum PoseFlags : byte { None = 0, Crouch = 1, Sprint = 2, Guard = 4, OnFloor = 8, Downed = 16, Bound = 32 }

/// <summary>An enemy as the server last saw it.</summary>
public readonly record struct EntityState(int NetId, NetVec Position, float Yaw, float Hp, byte State, byte Flags)
{
    public const byte FlagAlive = 1, FlagHostile = 2, FlagAware = 4, FlagBoss = 8, FlagInWater = 16;
}

public readonly record struct NpcState(string Id, NetVec Position, float Yaw, byte Flags)
{
    public const byte FlagDead = 1, FlagEscorting = 2, FlagRescued = 4, FlagDowned = 8, FlagCaptured = 16, FlagJailed = 32;
}

public readonly record struct PlayerPoseState(int Member, NetVec Position, float Yaw, float Pitch, NetVec Velocity, PoseFlags Flags);

public readonly record struct MemberVitals(int Member, float Hp, float Stamina, float Mana, float Cooldown, bool Alive);

/// <summary>Hand-written binary layouts for the three messages sent many times a second.</summary>
public static class BinaryLayout
{
    public static void Write(BinaryWriter w, NetVec v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    public static NetVec ReadVec(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    public static void Write(BinaryWriter w, Pose p)
    {
        w.Write(p.Tick); w.Write((short)p.Member); Write(w, p.Position); w.Write(p.Yaw); w.Write(p.Pitch); Write(w, p.Velocity); w.Write((byte)p.Flags);
    }

    public static Pose ReadPose(BinaryReader r)
        => new(r.ReadInt32(), r.ReadInt16(), ReadVec(r), r.ReadSingle(), r.ReadSingle(), ReadVec(r), (PoseFlags)r.ReadByte());

    public static void Write(BinaryWriter w, WorldSnapshot s)
    {
        w.Write(s.Tick); w.Write(s.ServerTime);
        w.Write((ushort)s.Enemies.Length);
        foreach (var e in s.Enemies) { w.Write(e.NetId); Write(w, e.Position); w.Write(e.Yaw); w.Write(e.Hp); w.Write(e.State); w.Write(e.Flags); }
        w.Write((ushort)s.Npcs.Length);
        foreach (var n in s.Npcs) { w.Write(n.Id); Write(w, n.Position); w.Write(n.Yaw); w.Write(n.Flags); }
        w.Write((byte)s.Players.Length);
        foreach (var p in s.Players) { w.Write((short)p.Member); Write(w, p.Position); w.Write(p.Yaw); w.Write(p.Pitch); Write(w, p.Velocity); w.Write((byte)p.Flags); }
    }

    public static WorldSnapshot ReadSnapshot(BinaryReader r)
    {
        var tick = r.ReadInt32(); var time = r.ReadDouble();
        var enemies = new EntityState[r.ReadUInt16()];
        for (var i = 0; i < enemies.Length; i++) enemies[i] = new EntityState(r.ReadInt32(), ReadVec(r), r.ReadSingle(), r.ReadSingle(), r.ReadByte(), r.ReadByte());
        var npcs = new NpcState[r.ReadUInt16()];
        for (var i = 0; i < npcs.Length; i++) npcs[i] = new NpcState(r.ReadString(), ReadVec(r), r.ReadSingle(), r.ReadByte());
        var players = new PlayerPoseState[r.ReadByte()];
        for (var i = 0; i < players.Length; i++) players[i] = new PlayerPoseState(r.ReadInt16(), ReadVec(r), r.ReadSingle(), r.ReadSingle(), ReadVec(r), (PoseFlags)r.ReadByte());
        return new WorldSnapshot(tick, time, enemies, npcs, players);
    }

    public static void Write(BinaryWriter w, Vitals v)
    {
        w.Write((byte)v.Members.Length);
        foreach (var m in v.Members) { w.Write((short)m.Member); w.Write(m.Hp); w.Write(m.Stamina); w.Write(m.Mana); w.Write(m.Cooldown); w.Write(m.Alive); }
    }

    public static Vitals ReadVitals(BinaryReader r)
    {
        var members = new MemberVitals[r.ReadByte()];
        for (var i = 0; i < members.Length; i++) members[i] = new MemberVitals(r.ReadInt16(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadBoolean());
        return new Vitals(members);
    }
}

/// <summary>Enemy AI state names as one byte, for the snapshot. Unknown names map to 0 (Idle).</summary>
public static class EnemyStateCodes
{
    public static readonly string[] Names = { "Idle", "Chase", "Attack", "Shoot", "Signature", "Flee", "Dead", "Guard", "Return", "Patrol", "AlarmRun", "Regroup", "Investigate", "Hunt", "Escort" };

    public static byte Encode(string name)
    {
        var colon = name.IndexOf(':');
        if (colon > 0) name = name[..colon];
        var i = Array.IndexOf(Names, name);
        return (byte)Math.Max(0, i);
    }

    public static string Decode(byte code) => code < Names.Length ? Names[code] : Names[0];
    public static bool IsUnaware(byte code) => Decode(code) is "Idle" or "Guard" or "Patrol" or "Return";
}
