// Test infrastructure only, never shipped and never installed in the player's Mods: the SurvivalcraftAPI 1.9.3.1 port of
// the MP snapshot's Survivalcraft.TestAutomation mod, so the single-player scripts (tools/MpM0/sp_*.py) drive an ISOLATED
// copy of the 1.9.3.1 game (standalone acceptance, AGENTS.md 2026-10-01) through the same loopback protocol:
//   command port (UDP, 127.0.0.1 only): KEYDOWN/KEYUP <key>, MOUSEMOVE x y, MOUSEDELTA dx dy, MOUSEDOWN/MOUSEUP <button> x y,
//     MOVE_INPUT x y z frames, INTERACT_RAY / HIT_RAY px py pz dx dy dz, DIG_RAY / AIM_RAY px py pz dx dy dz frames,
//     DROP_ONCE, CLICK_WIDGET <name or text>, FUNC <C#> (answers "OK:<json>" / "ERROR:<message>"), EXEC <C#> ("OK:null").
//     FUNC/EXEC compile the snippet with Roslyn against every loaded assembly, on the main thread, as the MP mod does; a
//     compiled snippet is cached by its exact text (2026-10-01: a compile blocked the main thread ~100 ms per command).
//     FUNCA <json string array>\n<code>: the same, with the arguments read through ScArgs.F/I/S(index), so a snippet whose
//     numbers change every call (a camera placement, a capture name) compiles once.
//   log port (UDP): every engine log line, Verbose included (the scripts wait for 'Entered screen "..."', a Verbose line).
// The 1.9.3.1 program takes no arguments, so the ports arrive as SC_TEST_CMD_PORT / SC_TEST_LOG_PORT environment variables
// (--cmd-port / --log-port arguments are honoured too). Physical keyboard and mouse input is muted while the mod is
// loaded (Harmony), so an unattended run is not disturbed; injected input goes through the engine's own Process* paths.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Engine;
using Engine.Input;
using Game;
using HarmonyLib;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace ScTestAutomation131;

/// <summary>The arguments of the FUNCA call being run (main thread only).</summary>
public static class ScArgs {
    public static string[] A = [];
    public static float F(int i) => float.Parse(A[i], CultureInfo.InvariantCulture);
    public static int I(int i) => int.Parse(A[i], CultureInfo.InvariantCulture);
    public static string S(int i) => A[i];
}

public sealed class UdpLogSink : ILogSink {
    readonly Socket m_socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    readonly IPEndPoint m_target;
    public UdpLogSink(int port) => m_target = new IPEndPoint(IPAddress.Loopback, port);
    public void Log(LogType type, string message) {
        try {
            string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + type + "] " + message;
            if (line.Length > 60000) line = line.Substring(0, 60000);
            m_socket.SendTo(Encoding.UTF8.GetBytes(line + "\n"), m_target);
        } catch { }   // a sink never logs
    }
}

public static class InjectedInput {
    static readonly FieldInfo MouseMovementField = AccessTools.Field(typeof(Mouse), "<MouseMovement>k__BackingField");
    public static bool IsInjectingMouseMove;
    public static Point2? QueuedMouseMovement;
    public static Vector3 HeldMoveInput; public static int HeldMoveInputEndFrame = -1;
    public static Ray3? QueuedInteractRay, QueuedHitRay, HeldDigRay, HeldAimRay;
    public static int HeldDigRayEndFrame = -1, HeldAimRayEndFrame = -1;
    public static bool QueuedDrop;

    public static void SetMouseMovement(Point2 movement) => MouseMovementField?.SetValue(null, movement);
    public static void HoldMoveInput(Vector3 move, int frames) { HeldMoveInput = move; HeldMoveInputEndFrame = Time.FrameIndex + Math.Max(1, frames); }
    public static void HoldDigRay(Ray3 ray, int frames) { HeldDigRay = ray; HeldDigRayEndFrame = Time.FrameIndex + Math.Max(1, frames); }
    public static void HoldAimRay(Ray3 ray, int frames) { HeldAimRay = ray; HeldAimRayEndFrame = Time.FrameIndex + Math.Max(1, frames); }

    public static bool TryGetHeldMoveInput(out Vector3 move) {
        if (Time.FrameIndex < HeldMoveInputEndFrame) { move = HeldMoveInput; return true; }
        move = Vector3.Zero; return false;
    }
    public static bool TryConsume(ref Ray3? slot, out Ray3 ray) {
        if (slot.HasValue) { ray = slot.Value; slot = null; return true; }
        ray = default; return false;
    }
    public static bool TryGetHeld(ref Ray3? slot, int endFrame, out Ray3 ray) {
        if (slot.HasValue && Time.FrameIndex < endFrame) { ray = slot.Value; return true; }
        slot = null; ray = default; return false;
    }
}

// Physical input muted; the injected paths (Keyboard.ProcessKeyDown/Up, Mouse.ProcessMouseDown/Up/Move) stay open.
[HarmonyPatch(typeof(Keyboard), "KeyDownHandler")] static class MuteKeyDown { static bool Prefix() => false; }
[HarmonyPatch(typeof(Keyboard), "KeyUpHandler")] static class MuteKeyUp { static bool Prefix() => false; }
[HarmonyPatch(typeof(Mouse), "MouseDownHandler")] static class MuteMouseDown { static bool Prefix() => false; }
[HarmonyPatch(typeof(Mouse), "MouseUpHandler")] static class MuteMouseUp { static bool Prefix() => false; }
[HarmonyPatch(typeof(Mouse), "ProcessMouseMove")] static class MuteMouseMove { static bool Prefix() => InjectedInput.IsInjectingMouseMove; }
[HarmonyPatch(typeof(Mouse), "BeforeFrame")]
static class RelativeMouseMovement {
    static void Postfix() {
        Point2 movement = InjectedInput.QueuedMouseMovement ?? Point2.Zero;
        InjectedInput.QueuedMouseMovement = null;
        InjectedInput.SetMouseMovement(movement);
    }
}
[HarmonyPatch(typeof(ComponentInput), "Update")]
static class InjectedPlayerInput {
    static void Postfix(ComponentInput __instance) {
        // The first player of the world (1.9.3.1 numbers players from 1, so the index is no "main player" test).
        PlayerData data = __instance.m_componentPlayer?.PlayerData;
        if (data == null || data.SubsystemPlayers == null || data.SubsystemPlayers.PlayersData.Count == 0 || data.SubsystemPlayers.PlayersData[0] != data) return;
        if (InjectedInput.TryGetHeldMoveInput(out Vector3 move)) {
            __instance.m_playerInput.Move = move; __instance.m_playerInput.CrouchMove = move;
            __instance.m_playerInput.CameraMove = move; __instance.m_playerInput.CameraCrouchMove = move;
        }
        if (InjectedInput.TryConsume(ref InjectedInput.QueuedInteractRay, out Ray3 interact)) __instance.m_playerInput.Interact = interact;
        if (InjectedInput.TryConsume(ref InjectedInput.QueuedHitRay, out Ray3 hit)) __instance.m_playerInput.Hit = hit;
        if (InjectedInput.TryGetHeld(ref InjectedInput.HeldDigRay, InjectedInput.HeldDigRayEndFrame, out Ray3 dig)) __instance.m_playerInput.Dig = dig;
        if (InjectedInput.TryGetHeld(ref InjectedInput.HeldAimRay, InjectedInput.HeldAimRayEndFrame, out Ray3 aim)) __instance.m_playerInput.Aim = aim;
        if (InjectedInput.QueuedDrop) { InjectedInput.QueuedDrop = false; __instance.m_playerInput.Drop = true; }
    }
}

public sealed class TestAutomation131Loader : ModLoader {
    UdpClient m_udp;
    readonly CancellationTokenSource m_cts = new();
    readonly Queue<string> m_pendingClicks = new();
    int m_lastClickFrame = -1;

    static int Port(string variable, string argument) {
        if (int.TryParse(Environment.GetEnvironmentVariable(variable), out int port)) return port;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == argument && int.TryParse(args[i + 1], out port)) return port;
        return 0;
    }

    public override void __ModInitialize() {
        new Harmony("zh667.ScTestAutomation131").PatchAll(Assembly.GetExecutingAssembly());
        ModsManager.RegisterHook("BeforeWidgetUpdate", this);
        ModsManager.RegisterHook("AfterWidgetUpdate", this);
        SettingsManager.MusicVolume = 0; SettingsManager.SoundsVolume = 0;
        SettingsManager.FileAssociationEnabled = false;
        SettingsManager.MultithreadedTerrainUpdate = false;
        Log.MinimumLogType = LogType.Verbose;
        int logPort = Port("SC_TEST_LOG_PORT", "--log-port");
        if (logPort > 0) Log.AddLogSink(new UdpLogSink(logPort));
        int cmdPort = Port("SC_TEST_CMD_PORT", "--cmd-port");
        if (cmdPort <= 0) cmdPort = 23944;
        StartUdpServer(cmdPort);
        Log.Information($"[ScTestAutomation131] initialized: isolated 1.9.3.1 copy, command port {cmdPort}, log port {logPort}");
    }

    void StartUdpServer(int port) {
        try {
            m_udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            Task.Run(async () => {
                while (!m_cts.IsCancellationRequested) {
                    try {
                        UdpReceiveResult received = await m_udp.ReceiveAsync(m_cts.Token);
                        if (!IPAddress.IsLoopback(received.RemoteEndPoint.Address)) continue;
                        string message = Encoding.UTF8.GetString(received.Buffer).Trim();
                        IPEndPoint remote = received.RemoteEndPoint;
                        Dispatcher.Dispatch(() => ProcessCommand(message, remote));
                    } catch (OperationCanceledException) { break; } catch (Exception ex) { Log.Warning("[ScTestAutomation131] receive: " + ex.Message); }
                }
            }, m_cts.Token);
        } catch (Exception ex) { Log.Error("[ScTestAutomation131] command port failed: " + ex.Message); }
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    void Reply(IPEndPoint remote, string text) { byte[] bytes = Encoding.UTF8.GetBytes(text); m_udp.Send(bytes, bytes.Length, remote); }

    void ProcessCommand(string command, IPEndPoint remote) {
        if (command.StartsWith("FUNCA ", StringComparison.Ordinal)) {
            int nl = command.IndexOf('\n');
            try {
                if (nl < 0) throw new ArgumentException("FUNCA needs a JSON argument line, a newline, then the code");
                ScArgs.A = System.Text.Json.JsonSerializer.Deserialize<string[]>(command.Substring(6, nl - 6)) ?? [];
                Reply(remote, "OK:" + System.Text.Json.JsonSerializer.Serialize(Evaluate(command.Substring(nl + 1))));
            }
            catch (Exception ex) { Reply(remote, "ERROR:" + ex.Message); Log.Error($"[ScTestAutomation131] FUNCA: {ex.Message}"); }
            return;
        }
        string[] p = command.Split(' ');
        if (p.Length == 0) return;
        string action = p[0].ToUpperInvariant();
        try {
            switch (action) {
                case "KEYDOWN" when p.Length >= 2: if (Enum.TryParse(p[1], true, out Key down)) Keyboard.ProcessKeyDown(down); break;
                case "KEYUP" when p.Length >= 2: if (Enum.TryParse(p[1], true, out Key up)) Keyboard.ProcessKeyUp(up); break;
                case "MOUSEMOVE" when p.Length >= 3:
                    InjectedInput.IsInjectingMouseMove = true;
                    try { Mouse.ProcessMouseMove(new Point2(I(p[1]), I(p[2]))); } finally { InjectedInput.IsInjectingMouseMove = false; }
                    break;
                case "MOUSEDELTA" when p.Length >= 3: InjectedInput.QueuedMouseMovement = new Point2(I(p[1]), I(p[2])); break;
                case "MOUSEDOWN" when p.Length >= 4: if (Enum.TryParse(p[1], true, out MouseButton bd)) Mouse.ProcessMouseDown(bd, new Point2(I(p[2]), I(p[3]))); break;
                case "MOUSEUP" when p.Length >= 4: if (Enum.TryParse(p[1], true, out MouseButton bu)) Mouse.ProcessMouseUp(bu, new Point2(I(p[2]), I(p[3]))); break;
                case "MOVE_INPUT" when p.Length >= 5: InjectedInput.HoldMoveInput(new Vector3(F(p[1]), F(p[2]), F(p[3])), I(p[4])); break;
                case "INTERACT_RAY" when p.Length >= 7: InjectedInput.QueuedInteractRay = new Ray3(new Vector3(F(p[1]), F(p[2]), F(p[3])), new Vector3(F(p[4]), F(p[5]), F(p[6]))); break;
                case "HIT_RAY" when p.Length >= 7: InjectedInput.QueuedHitRay = new Ray3(new Vector3(F(p[1]), F(p[2]), F(p[3])), new Vector3(F(p[4]), F(p[5]), F(p[6]))); break;
                case "DIG_RAY" when p.Length >= 8: InjectedInput.HoldDigRay(new Ray3(new Vector3(F(p[1]), F(p[2]), F(p[3])), new Vector3(F(p[4]), F(p[5]), F(p[6]))), I(p[7])); break;
                case "AIM_RAY" when p.Length >= 8: InjectedInput.HoldAimRay(new Ray3(new Vector3(F(p[1]), F(p[2]), F(p[3])), new Vector3(F(p[4]), F(p[5]), F(p[6]))), I(p[7])); break;
                case "DROP_ONCE": InjectedInput.QueuedDrop = true; break;
                case "CLICK_WIDGET" when p.Length >= 2: m_pendingClicks.Enqueue(command); return;
                case "FUNC" when p.Length >= 2: {
                    string code = string.Join(" ", p, 1, p.Length - 1);
                    try { Reply(remote, "OK:" + System.Text.Json.JsonSerializer.Serialize(Evaluate(code))); }
                    catch (Exception ex) { Reply(remote, "ERROR:" + ex.Message); throw; }
                    Log.Information("Command executed: " + command);
                    return;
                }
                case "EXEC" when p.Length >= 2: {
                    string code = string.Join(" ", p, 1, p.Length - 1);
                    try { Evaluate(code); Reply(remote, "OK:null"); }
                    catch (Exception ex) { Reply(remote, "ERROR:" + ex.Message); throw; }
                    break;
                }
            }
            Log.Information("Command executed: " + command);
        } catch (Exception ex) {
            Log.Error($"[ScTestAutomation131] command '{command}': {ex.Message}");
        }
    }

    static readonly Dictionary<string, ScriptRunner<object>> s_compiled = new();
    static ScriptOptions s_options; static int s_optionsAssemblies = -1;

    static object Evaluate(string code) {
        if (!s_compiled.TryGetValue(code, out ScriptRunner<object> runner)) {
            int loaded = AppDomain.CurrentDomain.GetAssemblies().Length;
            if (s_options is null || loaded != s_optionsAssemblies) { s_options = Options(); s_optionsAssemblies = loaded; }
            runner = CSharpScript.Create<object>(code, s_options).CreateDelegate();
            s_compiled[code] = runner;
        }
        return runner().GetAwaiter().GetResult();
    }

    static ScriptOptions Options() {
        IEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(ModsManager.Dlls.Values)
            .Concat(ModsManager.ModLoaders.Select(l => l.GetType().Assembly))
            .Where(a => !a.IsDynamic).Distinct();
        var references = new List<Microsoft.CodeAnalysis.MetadataReference>();
        foreach (Assembly assembly in assemblies) {
            try {
                if (!string.IsNullOrEmpty(assembly.Location)) references.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(assembly.Location));
                else {
                    unsafe {
                        if (System.Reflection.Metadata.AssemblyExtensions.TryGetRawMetadata(assembly, out byte* blob, out int length))
                            references.Add(Microsoft.CodeAnalysis.AssemblyMetadata.Create(Microsoft.CodeAnalysis.ModuleMetadata.CreateFromMetadata((IntPtr)blob, length)).GetReference());
                    }
                }
            } catch { }   // an assembly without readable metadata is not referenced
        }
        return ScriptOptions.Default.WithReferences(references).WithImports("System", "System.Linq", "System.Collections.Generic", "Engine", "Game");
    }

    // CLICK_WIDGET: the named (or so-captioned) widget of the current screen gets one injected click, in the frame in
    // which the engine updates that clickable widget, exactly as the MP mod does it.
    static Widget FindWidget(Widget root, string query) {
        if (root == null || !root.IsVisible || !root.IsEnabled) return null;
        if (string.Equals(root.Name, query, StringComparison.OrdinalIgnoreCase)) return root;
        if (root is ButtonWidget button && string.Equals(button.Text, query, StringComparison.OrdinalIgnoreCase)) return button;
        if (root is LabelWidget label && string.Equals(label.Text, query, StringComparison.OrdinalIgnoreCase)) return label;
        if (root is ContainerWidget container)
            foreach (Widget child in container.Children) { Widget found = FindWidget(child, query); if (found != null) return found; }
        return null;
    }

    static ClickableWidget Clickable(Widget widget) {
        if (widget == null) return null;
        if (widget is ClickableWidget clickable) return clickable;
        ClickableWidget own = Traverse.Create(widget).Field("m_clickableWidget").GetValue<ClickableWidget>();
        if (own != null) return own;
        if (widget is ContainerWidget container) { ClickableWidget child = container.Children.Find<ClickableWidget>(false); if (child != null) return child; }
        for (ContainerWidget parent = widget.ParentWidget; parent != null; parent = parent.ParentWidget) {
            ClickableWidget parents = Traverse.Create(parent).Field("m_clickableWidget").GetValue<ClickableWidget>();
            if (parents != null) return parents;
        }
        return null;
    }

    public override void BeforeWidgetUpdate(Widget widget) { }

    public override void AfterWidgetUpdate(Widget widget) {
        if (m_pendingClicks.Count == 0 || m_lastClickFrame == Time.FrameIndex || widget is not ClickableWidget clickable) return;
        string command = m_pendingClicks.Peek();
        string query = command.Substring(command.IndexOf(' ') + 1);
        if (Clickable(FindWidget(ScreensManager.CurrentScreen, query)) != clickable) return;
        clickable.IsPressed = false; clickable.IsTapped = true; clickable.IsClicked = true;
        if (clickable.IsAutoCheckingEnabled) clickable.IsChecked = !clickable.IsChecked;
        if (!string.IsNullOrEmpty(clickable.SoundName)) AudioManager.PlaySound(clickable.SoundName, 1f, 0f, 0f);
        m_pendingClicks.Dequeue(); m_lastClickFrame = Time.FrameIndex;
        Log.Information("Command executed: " + command);
    }

    public override void ModDispose() { m_cts.Cancel(); m_udp?.Close(); base.ModDispose(); }
}
