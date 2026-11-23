using System.Numerics;
using System.Text.Json;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Movement;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Husbandry.Checks")]
namespace Meitou.Husbandry;

internal sealed class HitchSystem(Post[] posts, Action<string> log) : ITickSystem, IDecisionOwner, IDecisionSource, IStateHashed, IModSaveData
{
    internal sealed record Identity(string Record, uint? Portrait, Hand? Handle);
    internal sealed record Saved(string Post, Identity Animal, bool Blocked);
    private sealed record SaveFile(int Version, Saved[] Assignments);
    private sealed class Assignment(Post post, Identity identity)
    {
        internal readonly Post Post = post;
        internal Identity Identity = identity;
        internal RunningTask? Walk;
        internal bool Blocked;
    }
    private readonly Dictionary<CharacterId, Assignment> assignments = [];
    private readonly Dictionary<string, Post> byKey = posts.ToDictionary(p => p.Key, StringComparer.Ordinal);
    private readonly List<Saved> pending = [];
    private Dictionary<CharacterId, HitchStatus> published = [];
    internal IReadOnlyList<Post> Posts => posts;
    internal HitchStatus? Lookup(CharacterId animal) => Volatile.Read(ref published).TryGetValue(animal, out var v) ? v : null;
    public string Name => "Husbandry: return to hitching post";
    public DecisionStep Step => DecisionStep.Fallback;
    // Orders, goals and jobs run before this fallback. The last stock source is ordinary idleness.
    public int Key => 2000 + (int)TaskType.Idle - 1;
    public void RegisterSources(DecisionSystem decisions) => decisions.Register(this);

    private static bool Owned(CharacterCold c) => c.IsPlayer && SquadRegistry.IsAnimal(c) && c.Medical?.Dead != true;
    private static bool CanWalk(CharacterCold c) => Owned(c) && c.Medical?.Incapacitated != true && c.CarriedBy.IsNone &&
        c.InSomething == HeldState.None && !c.InCombat;
    // Y selects a nearby floor. The animal stands on that floor, including on sloping ground.
    private static Vector3 StandingPoint(World world, Post post) =>
        new(post.Stand.X, world.Walkability.GroundHeight(post.Stand.X, post.Stand.Z, post.Stand.Y), post.Stand.Z);

    private static bool Inside(World world, Post post, Vector3 at)
    {
        var delta = new Vector2(at.X - post.Stand.X, at.Z - post.Stand.Z);
        return delta.LengthSquared() <= post.Radius * post.Radius &&
            Vector3.DistanceSquared(at, StandingPoint(world, post)) <= post.Radius * post.Radius;
    }
    private static Identity Identify(CharacterCold c) => new(c.RecordId, c.PortraitSerial,
        c.Save?.Source.Handle is { IsNull: false } h ? h : null);
    // Resolve identity before eligibility. An ineligible namesake must still make a serial-only match ambiguous.
    private static bool Matches(Identity key, CharacterCold c) => c.RecordId == key.Record &&
        (key.Handle is { } h ? c.Save?.Source.Handle == h && (key.Portrait == null || key.Portrait == c.PortraitSerial)
            : key.Portrait != null && key.Portrait == c.PortraitSerial);

    public void Inputs(World w, IReadOnlyList<SimCommand> commands)
    {
        if (pending.Count > 0) Restore(w);
        foreach (var cmd in commands.OfType<HitchCommand>())
        {
            if (!w.Characters.TryResolveNext(cmd.Animal, out var slot) || w.Characters.Cold(slot) is not { } c || !Owned(c))
            { Tell(w, "Select a living animal owned by your squad."); continue; }
            if (cmd.Assign) Assign(w, slot, cmd.Animal, c);
            else if (assignments.Remove(cmd.Animal, out var a))
            {
                if (OwnWalk(w, slot, a)) TaskStart.End(w, slot, TaskEnd.Stopped);
                DecisionSystem.Request(c);
                Tell(w, $"Released {c.Name} from {a.Post.Name}.");
            }
            else Tell(w, $"{c.Name} has no hitching post.");
        }
    }

    private void Assign(World w, int slot, CharacterId id, CharacterCold c)
    {
        if (assignments.TryGetValue(id, out var current)) { Tell(w, $"{c.Name} is already assigned to {current.Post.Name}."); return; }
        if (!CanWalk(c)) { Tell(w, "Choose a conscious, free animal outside combat."); return; }
        var from = w.Characters.Next[slot].Position;
        var near = posts.Where(p => Vector3.DistanceSquared(from, p.Base) <= 65 * 65)
            .OrderBy(p => Vector3.DistanceSquared(from, p.Base)).ThenBy(p => p.Key, StringComparer.Ordinal).ToArray();
        if (near.Length == 0) { Tell(w, "Move beside a hitching post."); return; }
        var p = near.FirstOrDefault(candidate => !assignments.Values.Any(a => a.Post.Key == candidate.Key));
        if (p == null) { Tell(w, "The nearby hitching post is occupied."); return; }
        if (!w.Walkability.IsWalkable(p.Stand.X, p.Stand.Z, p.Stand.Y))
        { Tell(w, $"The standing point at {p.Name} is blocked."); return; }
        var key = Identify(c);
        if (key.Handle == null && (key.Portrait == null || MatchingSlots(w, key).Count != 1))
        { Tell(w, "This animal has no unambiguous save identity."); return; }
        assignments.Add(id, new(p, key));
        DecisionSystem.Request(c);
        Tell(w, $"Assigned {c.Name} to {p.Name}.");
    }

    private static List<int> MatchingSlots(World w, Identity key)
    {
        var slots = new List<int>();
        for (int i = 0; i < w.Characters.HighWater; i++)
            if (w.Characters.Next[i].Alive && w.Characters.Cold(i) is { } c && Matches(key, c)) slots.Add(i);
        return slots;
    }

    private void Restore(World w)
    {
        foreach (var saved in pending)
        {
            var ids = MatchingSlots(w, saved.Animal);
            if (!byKey.TryGetValue(saved.Post, out var p) || ids.Count != 1)
            { Tell(w, $"Released unresolved hitch {saved.Post} ({ids.Count} matching animals)."); continue; }
            var slot = ids[0];
            var c = w.Characters.Cold(slot)!;
            if (!Owned(c))
            {
                Tell(w, $"Released hitch {saved.Post}. The saved animal is no longer a living squad animal.");
                continue;
            }
            if (assignments.TryAdd(w.Characters.IdOf(slot), new(p, Identify(c)) { Blocked = saved.Blocked }))
                DecisionSystem.Request(c);
        }
        pending.Clear();
    }

    private static bool OwnWalk(World w, int slot, Assignment a) => a.Walk != null &&
        ReferenceEquals(w.Characters.Cold(slot)?.Running, a.Walk) && (CharacterTask)w.Characters.Next[slot].Task == CharacterTask.GoTo;

    public DecisionCheck CanRun(World w, int slot)
    {
        if (!assignments.TryGetValue(w.Characters.IdOf(slot), out var a) || a.Blocked || w.Characters.Cold(slot) is not { } c ||
            !CanWalk(c) || c.Engaged || c.OrderQueue.Front != null) return default;
        if (OwnWalk(w, slot, a)) return DecisionCheck.Runs;
        if (a.Walk != null || Inside(w, a.Post, w.Characters.Next[slot].Position)) return default;
        var task = (CharacterTask)w.Characters.Next[slot].Task;
        if (task != CharacterTask.Idle && !(task == CharacterTask.Wander && c.Running == null)) return default;
        return DecisionCheck.Run;
    }

    public void Start(World w, int slot)
    {
        var a = assignments[w.Characters.IdOf(slot)];
        var at = StandingPoint(w, a.Post);
        if (w.Walkability.IsWalkable(at.X, at.Z, at.Y) && TaskStart.Walk(w, slot, at, SpeedMode.Walk, TaskType.MoveOnFreeWill, TaskSource.Goal) == StartResult.Started)
        {
            var animal = w.Characters.Cold(slot)!;
            // Nearby characters can satisfy an ordinary destination early. Close on the authored spot.
            animal.ArriveWithin = Math.Min(1.5f, a.Post.Radius / 2);
            a.Walk = animal.Running;
        }
        else Block(w, a, "The native walk could not start");
    }

    private void Block(World w, Assignment a, string reason)
    {
        a.Blocked = true;
        Tell(w, $"No route to the standing point at {a.Post.Name}. {reason}. Release and assign again after clearing it.");
    }

    public void SlowWorld(World w)
    {
        if (assignments.Count == 0 && published.Count == 0) return;
        var next = new Dictionary<CharacterId, HitchStatus>();
        foreach (var (id, a) in assignments.ToArray())
        {
            if (!w.Characters.TryResolveNext(id, out var i) || w.Characters.Cold(i) is not { } c || !Owned(c))
            { assignments.Remove(id); continue; }
            if (a.Identity.Handle == null && c.Save?.Source.Handle is { IsNull: false }) a.Identity = Identify(c);
            bool inside = Inside(w, a.Post, w.Characters.Next[i].Position);
            if (a.Walk != null && !OwnWalk(w, i, a))
            {
                if (!inside && c.WalkEnd is WalkEnd.Failed or WalkEnd.Finished && (CharacterTask)w.Characters.Next[i].Task == CharacterTask.Idle)
                    Block(w, a, $"The walk ended {c.WalkEnd} at {Vector3.Distance(StandingPoint(w, a.Post), w.Characters.Next[i].Position):F1} units");
                a.Walk = null;
                DecisionSystem.Request(c);
            }
            next.Add(id, new(a.Post.Key, a.Post.Name, inside));
        }
        Volatile.Write(ref published, next);
    }

    private void Tell(World w, string message) { w.Post(message); log(message); }
    private Saved[] Entries() => assignments.Values.Select(a => new Saved(a.Post.Key, a.Identity, a.Blocked)).Concat(pending)
        .OrderBy(a => a.Post, StringComparer.Ordinal).ToArray();
    public void Write(Stream stream) => JsonSerializer.Serialize(stream, new SaveFile(1, Entries()));
    public void Read(Stream stream)
    {
        var data = JsonSerializer.Deserialize<SaveFile>(stream);
        if (data is not { Version: 1, Assignments: not null } || data.Assignments.Any(a => a?.Animal == null || string.IsNullOrEmpty(a.Post)) ||
            data.Assignments.Select(a => a.Post).Distinct(StringComparer.Ordinal).Count() != data.Assignments.Length)
            throw new InvalidDataException("Invalid Husbandry hitch save.");
        pending.Clear();
        pending.AddRange(data.Assignments);
    }
    public void Hash(ref StateHasher h)
    {
        foreach (var entry in Entries())
        {
            AddText(ref h, entry.Post); AddText(ref h, entry.Animal.Record);
            h.Add(entry.Animal.Portrait.HasValue); h.Add((ulong)(entry.Animal.Portrait ?? 0)); h.Add(entry.Blocked);
            h.Add(entry.Animal.Handle.HasValue);
            if (entry.Animal.Handle is { } hand) { h.Add(hand.Type); h.Add(hand.C); h.Add(hand.Cs); h.Add(hand.I); h.Add(hand.S); }
        }
        foreach (var (id, a) in assignments.OrderBy(p => p.Key.Slot)) { h.Add(id); h.Add(a.Walk?.Tick ?? -1); }
    }
    private static void AddText(ref StateHasher h, string value) { h.Add(value.Length); foreach (char c in value) h.Add((int)c); }
}
