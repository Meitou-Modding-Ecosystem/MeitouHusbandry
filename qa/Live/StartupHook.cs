using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Movement;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using HarmonyLib;
using Meitou.Data.Save;
using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Simulation;

internal static partial class StartupHook
{
    static string mode = "", folder = "";
    const string Garru = "42201-rebirth.mod";
    static string stage = "start";
    static long since, report, startTick;
    static World? savedWorld;
    static Vector3 stand, goal, handlerGoal, far;
    static uint first, second;
    static int captureIndex;
    static long lastCapture = -50;
    static float furthest;
    static string key = "";
    static bool done;
    static string? answeredConversation;

    public static void Initialize()
    {
        mode = Environment.GetEnvironmentVariable("HITCH_TRIAL_MODE") ?? "";
        if (mode is not ("core" or "seed" or "prepare" or "ride" or "two")) return;
        folder = Environment.GetEnvironmentVariable("HITCH_SAVE") ?? throw new Exception("Missing QA folder");
        var renderer = typeof(IMod).Assembly.GetType("Meitou.Game.Rendering.Characters.CharacterRenderer", true)!;
        new Harmony("husbandry.qa.observation").Patch(AccessTools.Method(renderer, "CollectBolts"),
            postfix: new HarmonyMethod(typeof(StartupHook), nameof(Frame)));
        Log("Armed " + mode);
    }
    static object? Field(object o, string name) => AccessTools.Field(o.GetType(), name).GetValue(o);
    static object? Property(object o, string name) => AccessTools.Property(o.GetType(), name).GetValue(o);
    static void Set(object o, string name, object value) => AccessTools.Field(o.GetType(), name).SetValue(o, value);
    static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));
    static void Log(string s) => Console.WriteLine("HITCH QA " + s);
    static void Change(string next, long tick) { stage = next; since = Stopwatch.GetTimestamp(); startTick = tick; Log($"{tick} {next}"); }
    static void Must(bool truth, string why) { if (!truth) throw new InvalidDataException(why); }
    static void End(bool ok, string why) { done = true; Log((ok ? "PASS " : "FAIL ") + why); }
    static void Shot(object host, string name)
    {
        Set(Field(host, "AgentChannel")!, "ShotPath", Path.GetFullPath(name + ".png"));
        Set(host, "ScreenshotRequested", true);
    }
    static void Action(object host, string label)
    {
        var menu = Field(host, "DebugMenu")!; var apply = AccessTools.Method(menu.GetType(), "DebugApply");
        var rows = (IReadOnlyList<ModDebugCommand>)Property(Field(host, "Modding")!, "ModDebugRows")!;
        int stock = ((string[])AccessTools.Field(menu.GetType(), "DebugLabels").GetValue(null)!).Length;
        int index = Enumerable.Range(0, rows.Count).Single(i => rows[i].Owner == "meitou.husbandry" && rows[i].Label == label);
        var row = Enum.ToObject(apply.GetParameters()[0].ParameterType, stock + index);
        string? reply = (string?)apply.Invoke(menu, [row, "", default((string?, string)), null]);
        Log($"F10 {label}: {reply}"); Must(reply?.EndsWith("queued.") == true, "F10 refused selection");
    }
    static bool Written(object saves)
    {
        if (!Equals(Field(saves, "SaveInUse"), folder) || Field(saves, "SaveWrite") is not Task { IsCompleted: true } t) return false;
        t.GetAwaiter().GetResult(); return File.Exists(Path.Combine(folder, SaveFolder.QuickSaveName));
    }
    static void Frame(object __instance)
    {
        if (done || AccessTools.Property(__instance.GetType(), "Source").GetValue(__instance) is not Delegate { Target: { } host }
            || host.GetType().Name != "GameHost" || Field(host, "Session") is not WorldSession session) return;
        try
        {
            session.Simulation.SetSpeed(1);
            var w = session.World; var snap = session.CurrentSnapshot; long tick = snap.Tick;
            // The stock gate inspection can pause this field visit. Answer as a player; never disable dialogue or clear the pause flag.
            if (snap.Conversation is { } conversation)
            {
                string signature = conversation.Npc + ":" + conversation.Text + ":" + string.Join("|", conversation.Choices);
                if (answeredConversation != signature)
                {
                    int accept = Enumerable.Range(0, conversation.Choices.Count)
                        .FirstOrDefault(i => conversation.Choices[i].StartsWith("Okay, go ahead", StringComparison.OrdinalIgnoreCase), -1);
                    if (accept >= 0)
                    {
                        Log("Stock gate inspection: accepted the belongings check through DialogueChoice.");
                        w.Commands.Enqueue(new DialogueChoice(accept) { Tick = w.Tick });
                    }
                    else if (conversation.Choices.Count == 1 && conversation.Choices[0] == "[Leave]")
                    {
                        Log("Stock gate inspection: selected [Leave] after the clean check.");
                        w.Commands.Enqueue(new DialogueChoice(0) { Tick = w.Tick });
                    }
                    else if (conversation.Choices.Count == 0)
                    {
                        Log("Closing completed stock conversation: " + conversation.Text);
                        w.Commands.Enqueue(new EndConversation { Tick = w.Tick });
                    }
                    else throw new InvalidDataException("Unanticipated stock conversation: " + conversation.Text + " / " + string.Join(" | ", conversation.Choices));
                    answeredConversation = signature;
                }
                return;
            }
            answeredConversation = null;
            var party = snap.Characters.Where(c => c.IsPlayer).ToArray(); if (party.Length == 0) return;
            object? system = w.Systems.SingleOrDefault(s => s.GetType().FullName == "Meitou.Husbandry.HitchSystem");
            if (mode == "core")
            {
                Must(system != null, "framework unavailable"); Must(!((IEnumerable)Property(system!, "Posts")!).Cast<object>().Any(), "framework added posts");
                var rows = (IReadOnlyList<ModDebugCommand>)Property(Field(host, "Modding")!, "ModDebugRows")!;
                Must(!rows.Any(x => x.Owner == "meitou.husbandry"), "framework-only F10 actions");
                End(true, "framework loaded alone, zero post instances and zero hitching rows"); return;
            }
            var g = party.Where(c => c.RecordId == Garru).OrderBy(c => c.PortraitSerial).ToArray();
            if (since == 0) since = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(since).TotalSeconds > 115) throw new TimeoutException($"{stage}; party={string.Join(",", party.Select(c => c.RecordId))}");
            if (g.Length != 2 || tick < 3) return;
            var human = party.Single(c => c.RecordId != Garru);
            var saves = Field(host, "Saves")!;
            if (mode is "seed" or "prepare")
            {
                if (stage == "start")
                {
                    if (mode == "prepare")
                    {
                        Must(system != null && ((IEnumerable)Property(system, "Posts")!).Cast<object>().Count() == 1, "expected the two-mod profile");
                        foreach (var animal in g) Must(AccessTools.Method(system!.GetType(), "Lookup").Invoke(system, [animal.Id]) == null, "expected unassigned starter animals");
                    }
                    Log($"Handler {human.RecordId}; Garru serials {g[0].PortraitSerial}/{g[1].PortraitSerial}; at {g[0].Position}");
                    Set(saves, "SaveRequestedFolder", folder); Change("saving field visit", tick);
                }
                else if (Written(saves)) End(true, "wrote portable native two-Garru starter save");
                return;
            }
            if (stage == "load" && ReferenceEquals(w, savedWorld)) return;
            Must(system != null, "Husbandry failed to load");
            var posts = ((IEnumerable)Property(system!, "Posts")!).Cast<object>().ToArray();
            if (mode == "two") { TwoPosts(host, session, system!, posts, g[0], g[1]); return; }
            Must(posts.Length == 1, $"Expected one placed post; actual count {posts.Length}.");
            var post = posts[0];
            var a = g[0]; var b = g[1];
            object? Hitch(CharacterSnapshot c) => AccessTools.Method(system!.GetType(), "Lookup").Invoke(system, [c.Id]);
            bool At(object? hitch) => hitch != null && (bool)Property(hitch, "AtPost")!;
            var status = Hitch(a); float d = Flat(a.Position, stand);
            void Move(CharacterSnapshot c, Vector3 p) => w.Commands.Enqueue(new MoveOrder([c.Id], p) { Tick = w.Tick });
            void Select(CharacterSnapshot c) => w.Commands.Enqueue(new SelectCommand([c.Id]) { Tick = w.Tick });
            Vector3 Point(float x, float z) => new(x, w.Walkability.GroundHeight(x, z), z);
            void Go(string step) => Change(step, tick);
            void Camera() => session.Camera.Place(new(stand.X + 29, stand.Z + 13), 1.28f, .44f, 193);
            if (Stopwatch.GetElapsedTime(report).TotalSeconds > 6)
            {
                var cold = w.Characters.Cold(a.Id.Slot); Log($"tick {tick} {stage} first={a.Position} task={a.Task} row={cold?.Running?.Row} order={cold?.OrderQueue.Front?.Row} trace={cold?.Decision?.Winner} inside={At(status)} d={d:F1}"); report = Stopwatch.GetTimestamp();
            }
            if (stage != "away" && stage != "revisit" && stage != "start") Camera();
            if (stage is not ("start" or "save" or "load" or "away" or "revisit" or "assign sibling" or "reuse") &&
                (tick < lastCapture || tick - lastCapture >= 4))
            {
                Directory.CreateDirectory("frames");
                string file = $"frames/hitch-{++captureIndex:D4}.png";
                Shot(host, Path.ChangeExtension(file, null));
                File.AppendAllText("capture.tsv", $"{file}\t{tick}\t{stage}\t{d:F1}\n");
                lastCapture = tick;
            }
            switch (stage)
            {
                case "start":
                    w.Decisions.Tracing = true;
                    stand = (Vector3)Property(post, "Stand")!; key = (string)Property(post, "Key")!;
                    first = a.PortraitSerial; second = b.PortraitSerial;
                    Must(first != second && status == null && Hitch(b) == null, "starter must have two distinct unassigned Garru");
                    Log($"Placed post {key} base={Property(post, "Base")} stand={stand} first={first} second={second}; handler={human.RecordId}");
                    goal = Point(stand.X + 84, stand.Z + 4); handlerGoal = Point(stand.X + 18, stand.Z + 93);
                    Must(w.Walkability.IsWalkable(stand.X, stand.Z, stand.Y), "standing point is blocked");
                    Select(a); Go("assign"); break;
                case "assign":
                    if (!a.Selected) break;
                    Action(host, "Assign to nearby hitching post"); Go("settle"); break;
                case "settle":
                    if (!At(status)) break;
                    Action(host, "Assign to nearby hitching post"); Select(b); Shot(host, "01-assigned"); Go("occupied"); break;
                case "occupied":
                    if (!b.Selected) break;
                    Action(host, "Assign to nearby hitching post"); Go("check occupied"); break;
                case "check occupied":
                    if (tick - startTick < 12) break;
                    Must(At(status) && Hitch(b) == null, "second Garru took occupied post");
                    Select(a); Move(human, handlerGoal); Move(b, Point(stand.X - 12, stand.Z + 68)); Go("handler away"); break;
                case "handler away":
                    if (Flat(human.Position, handlerGoal) > 8) break;
                    Must(At(status), "handler movement took assigned animal"); Move(a, goal); Go("direct order"); break;
                case "direct order":
                    furthest = Math.Max(furthest, d);
                    if (Flat(a.Position, goal) > 8) break;
                    Must(!At(status) && status != null, "direct order failed to leave radius");
                    Shot(host, "02-player-order"); Log($"reached player destination, distance from stand={d:F1}"); Go("return"); break;
                case "return":
                    furthest = Math.Max(furthest, d);
                    if (!At(status)) break;
                    Shot(host, "03-returned"); Log($"returned without another player order; maximum excursion {furthest:F1}");
                    savedWorld = w; Set(saves, "SaveRequestedFolder", folder); Go("save"); break;
                case "save":
                    if (!Written(saves)) break;
                    Must(File.Exists(Path.Combine(folder, SaveFolder.ModDataName("meitou.husbandry"))), "missing sidecar");
                    Must(At(status), "native save disrupted assignment"); Set(saves, "LoadRequestedFolder", folder); Go("load"); break;
                case "load":
                    if (status == null) break;
                    Must(a.PortraitSerial == first && b.PortraitSerial == second && Hitch(b) == null && (string)Property(status, "PostKey")! == key, "wrong post or individual after reload");
                    Log($"restored exact Garru {first}; same-record sibling {second} free"); Select(a); Move(a, goal); Go("second order"); break;
                case "second order":
                    if (Flat(a.Position, goal) > 8) break;
                    Go("release on return"); break;
                case "release on return":
                    if (d > 57 || d < 19 || !a.Selected || a.Task != CharacterTask.GoTo) break;
                    Log($"releasing while returning, distance={d:F1}, row={w.Characters.Cold(a.Id.Slot)?.Running?.Row}");
                    Action(host, "Release from hitching post"); Go("released"); break;
                case "released":
                    if (status != null) break; Move(a, goal); Go("walk away"); break;
                case "walk away":
                    if (Flat(a.Position, goal) > 8) break;
                    Go("stay released"); break;
                case "stay released":
                    if (tick - startTick < 75) break;
                    Must(status == null && Flat(a.Position, goal) < 16, "released animal returned unexpectedly");
                    Shot(host, "04-released");
                    far = Point(stand.X + 3000, stand.Z + 2400);
                    w.Commands.Enqueue(new DebugTeleport(party.Select(c => c.Id).ToArray(), far) { Tick = w.Tick });
                    session.Camera.Place(new(far.X, far.Z), 1.4f, .38f, 150); Go("away"); break;
                case "away":
                    if (Flat(a.Position, stand) < 2800 || tick - startTick < 52) break;
                    Log($"QA travel fixture: all party away, first {Flat(a.Position, stand):F0} units from post");
                    w.Commands.Enqueue(new DebugTeleport(party.Select(c => c.Id).ToArray(), stand) { Tick = w.Tick });
                    session.Camera.Place(new(stand.X, stand.Z), 1.4f, .35f, 125); Go("revisit"); break;
                case "revisit":
                    if (d > 30 || tick - startTick < 20) break;
                    Must(key == (string)Property(post, "Key")! && Hitch(b) == null && status == null, "post or release changed on revisit");
                    Select(b); Go("assign sibling"); break;
                case "assign sibling":
                    if (!b.Selected) break;
                    Action(host, "Assign to nearby hitching post"); Go("reuse"); break;
                case "reuse":
                    if (Hitch(b) == null) break;
                    Must(status == null, "first animal also assigned");
                    End(true, $"same post reusable; native orders, return, release, save/reload, revisit and sibling verified. key={key}, serials={first}/{second}"); break;
            }
        }
        catch (Exception ex) { End(false, $"{stage}: {ex.GetBaseException()}"); }
    }
}
