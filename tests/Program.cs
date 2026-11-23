using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation.Production;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Husbandry;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Player;

var single = Run(1); var parallel = Run(4);
if (!single.SequenceEqual(parallel)) throw new Exception("Thread-count comparison differed");
Console.WriteLine($"PASS one/four workers: {single.Count} matching tick hashes, final {single[^1]:x16}");

static List<ulong> Run(int workers)
{
    var hashes = new List<ulong>();
    var logs = new List<string>();
    var g = new Ground();
    Post[] posts = [new("39.16:first","Town post",Vector3.Zero,new(12,0,0),6),
                  new("39.16:second","Town post",new(165,0,0),new(177,0,0),6)];
    var sys = new HitchSystem(posts, logs.Add); var api = new HusbandryMod();
    var settings = new WorldSettings { Threads = workers, MinPartitionSize = 4, Seed = 784, PublishSnapshots = false };
    World Create(HitchSystem h) => new(settings, g, [new PlayerSystem(), new MovementSystem(new PathService(g, true)), h]);
    using var main = Create(sys); var w = main; w.Player.Faction = 1; w.Decisions.Tracing = true;
    CharacterId Spawn(float x, uint serial, bool owned = true, bool animal = true)
    {
        return w.Characters.Spawn(new CharacterHot { Position = new(x, 0, 0), MaxSpeed = 45, WalkSpeed = 15 }, new CharacterCold
        {
            Name = animal ? "Garru" : "Handler",
            RecordId = animal ? "42201-rebirth.mod" : "1533662-rebirth.mod",
            IsPlayer = owned,
            Faction = owned ? 1 : 2,
            Fighter = new Meitou.Simulation.Combat.Fighter(null) { IsAnimal = animal },
            Animal = animal ? new AnimalData { StringId = "42201-rebirth.mod" } : null,
            Race = new RaceData(),
            PortraitSerial = serial,
            Medical = new MedicalState([])
        }, w.Tick);
    }
    void Step(int n = 1) { for (int k = 0; k < n; k++) { w.RunTick(); hashes.Add(w.StateHash()); } }
    void Cmd(SimCommand c) { w.Commands.Enqueue(c); Step(); }
    void Hitch(CharacterId id, bool assign) => Cmd(new HitchCommand(id, assign) { Tick = w.Tick });
    void Move(CharacterId id, Vector3 to) => Cmd(new MoveOrder([id], to) { Tick = w.Tick });
    Vector3 At(CharacterId id) => w.Characters.Previous[id.Slot].Position;
    void Check(bool ok, string message)
    { if (!ok) throw new Exception($"{workers} workers tick {w.Tick}: {message}; {string.Join(" / ", logs.TakeLast(7))}"); }
    void Until(Func<bool> test, string why, int max = 1050)
    { for (int i = 0; i < max && !test(); i++) Step(); Check(test(), why); }
    var first = Spawn(34, 11); var second = Spawn(40, 22); var wild = Spawn(18, 33, false); var handler = Spawn(8, 44, true, false);
    for (int i = 0; i < 42; i++) Spawn(700 + i * 12, (uint)i + 100, false);
    Hitch(wild, true); Hitch(handler, true);
    Check(api.GetHitch(w, wild) == null && api.GetHitch(w, handler) == null, "unowned/human eligibility");
    g.Blocked = true; Hitch(first, true); g.Blocked = false;
    Check(api.GetHitch(w, first) == null && logs[^1].Contains("blocked"), "blocked point before assignment");
    Hitch(first, true); Hitch(first, true); Hitch(second, true);
    Check(api.GetHitch(w, first)?.PostKey == posts[0].Key && api.GetHitch(w, second) == null && logs[^1].Contains("occupied"), "occupancy");
    Until(() => api.GetHitch(w, first)?.AtPost == true, "initial fallback walk failed");
    Move(handler, new(110, 0, 15));
    Until(() => Vector3.Distance(At(handler), new(110, 0, 15)) < 5, "handler's order");
    Check(api.GetHitch(w, first)?.AtPost == true, "handler took animal");
    int requests = g.Requests; Step(120); Check(g.Requests == requests, "idle animal reissued paths");
    Move(first, new(85, 0, 0));
    Until(() => At(first).X > 81, "direct order failed"); Until(() => api.GetHitch(w, first)?.AtPost == true, "walking back after direct order");
    Move(second, new(165, 0, 0)); Until(() => At(second).X > 162, "travel to other instance");
    Hitch(second, true);
    Check(api.GetHitch(w, second)?.PostKey == posts[1].Key && api.GetHitch(w, first)?.PostKey == posts[0].Key, "same building, independent occupancy");
    using var save = new MemoryStream(); sys.Write(save);
    var again = new HitchSystem(posts, logs.Add); using var loaded = Create(again); w = loaded; w.Player.Faction = 1;
    second = Spawn(175, 22); first = Spawn(12, 11); save.Position = 0; again.Read(save); Step(3);
    Check(api.GetHitch(w, first)?.PostKey == posts[0].Key && api.GetHitch(w, second)?.PostKey == posts[1].Key, "unique serial+record restore after slot swap");
    Check(api.GetHitch(main, main.Characters.IdOf(0))?.PostKey == posts[0].Key, "independent worlds");
    Move(first, new(95, 0, 0)); Until(() => At(first).X > 92, "second outward order");
    Until(() => At(first).X < 65, "mid-return test");
    Hitch(first, false); var stop = At(first); Step(100);
    Check(api.GetHitch(w, first) == null && Vector3.Distance(At(first), stop) < 4, "release must cancel its own return");
    Move(first, new(16, 0, 0)); Until(() => At(first).X < 20, "released animal normal movement");
    Hitch(first, true); Move(first, new(85, 0, 0)); Step(8); Hitch(first, false);
    Until(() => At(first).X > 81, "release interrupted a player's order"); Step(75);
    Check(At(first).X > 80, "released animal tried to return");
    var unreachable = Spawn(-61, 66); g.Unreachable = true; int before = logs.Count;
    Hitch(unreachable, true);
    Until(() => logs.Skip(before).Any(l => l.Contains("No route")), "unreachable route diagnostic");
    requests = g.Requests; Step(220); Check(requests == g.Requests, "retried blocked path"); g.Unreachable = false;
    var ambiguous = new HitchSystem(posts, logs.Add); using var dupe = Create(ambiguous); w = dupe;
    var one = Spawn(29, 11); var two = Spawn(25, 11); var wildOriginal = Spawn(31, 22, false);
    save.Position = 0; ambiguous.Read(save); Step(2);
    Check(api.GetHitch(w, one) == null && api.GetHitch(w, two) == null, "ambiguous pair bound after load");
    Check(api.GetHitch(w, wildOriginal) == null, "restoring a sole unowned animal");
    var eligibleTwin = Spawn(20, 314); Spawn(30, 314, false);
    Hitch(eligibleTwin, true);
    Check(api.GetHitch(w, eligibleTwin) == null && logs[^1].Contains("unambiguous"), "serial collision with an ineligible animal");
    var fighting = Spawn(32, 73); w.Characters.Cold(fighting.Slot)!.InCombat = true;
    Hitch(fighting, true); Check(api.GetHitch(w, fighting) == null, "combat assignment accepted");
    var worker = Spawn(-28, 99);
    var work = new StandingJob();
    w.Decisions.RegisterJob(TaskType.MoveOnFreeWill, work);
    var cold = w.Characters.Cold(worker.Slot)!;
    cold.Jobs.Add(PermaJob.Untargeted(TaskType.MoveOnFreeWill));
    Hitch(worker, true); Step(45);
    Check(api.GetHitch(w, worker)?.AtPost == false && cold.Running?.Source == TaskSource.Job && At(worker).X < -20, "an active list job lost priority");
    work.Available = false; cold.InCombat = true; DecisionSystem.Request(cold); Step(60);
    Check(At(worker).X < -20 && !ambiguous.CanRun(w, worker.Slot).Can, "combat failed to suppress a return");
    cold.InCombat = false; DecisionSystem.Request(cold);
    Until(() => api.GetHitch(w, worker)?.AtPost == true, "return after jobs and combat clear");
    Hitch(worker, false);
    var stranded = Spawn(-32, 1001); g.Unreachable = true; g.Wall = true; before = logs.Count;
    Hitch(stranded, true);
    Until(() => logs.Skip(before).Any(l => l.Contains("No route")), "short obstructed path diagnostic", 1800);
    requests = g.Requests; Step(240);
    Check(g.Requests == requests && At(stranded).X < 0 && logs.Skip(before).Count(l => l.Contains("No route")) == 1, "blocked approach repeated or crossed obstruction");
    Console.WriteLine($"PASS {workers} workers; native movement, two instances, save/ambiguity, blocked path and order ownership");
    return hashes;
}
sealed class StandingJob : IJobSource
{
    // A fixture in the native jobs list holds its standing task while work is available.
    public bool Available = true;
    public DecisionCheck CanRun(World w, int slot, int k) => !Available ? default :
        w.Characters.Cold(slot)?.Running?.Source == TaskSource.Job ? DecisionCheck.Runs : DecisionCheck.Run;
    public void Start(World w, int slot, int k) => TaskStart.Start(w, slot, new TaskRequest(CharacterTask.Idle, TaskType.MoveOnFreeWill, TaskSource.Job) { Job = k });
    public void Stop(World w, int slot, int k) => TaskStart.End(w, slot, TaskEnd.Stopped);
}
sealed class Ground : IWalkability
{
    public bool Blocked, Unreachable, Wall;
    public int Requests;
    public float GroundHeight(float x, float z) => 0;
    public bool IsWalkable(float x, float z) => !Blocked && !(Wall && x >= -4 && x <= 3);
    public PathResult FindPath(Vector3 from, Vector3 to)
    { Interlocked.Increment(ref Requests); return Unreachable ? PathResult.NotFound : new([from, to]); }
}
