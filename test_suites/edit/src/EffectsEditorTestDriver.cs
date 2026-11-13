namespace Cthangover.Tools.EffectsEditor.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Collections.Generic;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Settings;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Effects.Atmosphere;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Time;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Interface.View;
    using Cthangover.Tools.Services;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.RpgEditor;
    using global::Mods.ModManager;
    using Godot;
    /// <summary>Exercises author-facing controls and real render materials. Writes only a backed-up fixture file, restored even on assertion failure.</summary>
    public partial class EffectsEditorTestDriver : Node
    {
        private int readinessToken = int.MinValue;
        private ulong readinessStarted;
        private ulong readinessFrame;
        private const string Mod = "test_defaults_and_patches";
        private const string ProfileId = "effects_editor_test/glow";
        private const string FileName = "atmospheres/test_defaults_and_patches.json";
        private EffectsEditorWindow window;
        private const string MapMod = "rpg_plus_game";
        private const string MapId = "rpg_elder_house";
        private const string MapPath = "maps/rpg_elder_house.json";
        private RpgEditorWindow mapWindow;
        private CollectionsGallery gallery;
        private string backup;
        private string diskPath;
        private int step, passed, failed;
        private double elapsed;
        private long clock;
        private bool clockSaved;
        private float darkBrightness;
        private LampBehaviour lamp;
        private RpgMap map;
        private string oldMode;
        private readonly string shots;
        public EffectsEditorTestDriver()
        {
            shots = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--shots="))?.Substring(8);
            var timer = new Timer { WaitTime = .05, Autostart = true }; AddChild(timer); timer.Timeout += Tick;
        }
        private void Tick()
        {
            if (step < 0) return;
            elapsed += .05;
            if (elapsed > 100) { Check(false, "timeout at step " + step); Finish(); return; }
            try { Run(); }
            catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
        }
        // Deferred UI/scene work must finish before the original assertions run.
        private bool WaitForReadyFrames(int token, bool ready, double timeoutSeconds = 30)
        {
            var now = global::Godot.Time.GetTicksMsec();
            if (readinessToken != token)
            {
                readinessToken = token;
                readinessStarted = now;
                readinessFrame = 0;
            }
            if (now - readinessStarted > timeoutSeconds * 1000)
                throw new global::System.TimeoutException($"readiness at step {token}: scene/UI did not become ready");
            if (!ready) { readinessFrame = 0; return false; }
            if (readinessFrame == 0) readinessFrame = global::Godot.Engine.GetProcessFrames();
            return global::Godot.Engine.GetProcessFrames() - readinessFrame >= 2;
        }

        private void Run()
        {
            if (!WaitForReadyFrames(step, Boot.IsReady && GetTree().CurrentScene != null && !GodotSceneService.IsTransitioning && (step == 0 || window != null && window.IsInsideTree()), 30)) return;
            if (!Boot.IsReady) return;
            switch (step)
            {
                case 0:
                    if (string.IsNullOrEmpty(GetTree().Root.GetNodeOrNull<SceneManager>("SceneManager")?.CurrentSceneName)) return;
                    var box = RpgInput.FindDialogBox();
                    if (box?.IsRunning == true) { if (box.IsWaitingAnswer) box.TrySelectVariant(0); else box.NextAction(); return; }
                    if (RpgWorldHost.Current?.View == null) return;
                    backup = ModFileService.ReadText(Mod, FileName); diskPath = ModFileService.GetWritablePath(Mod, FileName);
                    window = ToolWindow.Open<EffectsEditorWindow>(); window.OpenMod(Mod);
                    Press(window, "tools/entity_editor/new"); step++; return;
                case 1:
                    var idField = Find<LineEdit>(window, "EffectId");
                    idField.Text = ProfileId; idField.EmitSignal(LineEdit.SignalName.TextChanged, ProfileId);
                    Find<SpinBox>(window, "EmitterCount").Value = 37;
                    Press(window, "tools/effects_editor/glow");
                    Check(AtmosphereCatalog.Collect()[ProfileId].Emitters.Count == 2, "add source updates unsaved live profile");
                    Check(AtmosphereCatalog.Collect()[ProfileId].Emitters[0].Count == 37, "numeric form changes preview particle count");
                    Press(window, "tools/entity_editor/duplicate");
                    Check(window.Document.Records.Any(r => r.Id == ProfileId + "_copy"), "copy creates a separate profile");
                    Press(window, "tools/entity_editor/undo");
                    Check(!window.Document.Records.Any(r => r.Id == ProfileId + "_copy"), "undo removes copied profile");
                    var extensions = Find<CodeEdit>(window, "EmitterExtra");
                    extensions.Text = "{\"Heat\":42}"; extensions.EmitSignal(TextEdit.SignalName.TextChanged);
                    window.Document.Find(ProfileId).Data["AuthorExtension"] = 73;
                    Check(window.Save(), "save through the editor");
                    Check(Entities.Collect<AtmosphereProfile>()[ProfileId].Emitters[0].Count == 37, "runtime resolves saved profile immediately");
                    Check(Entities.Collect<AtmosphereProfile>()[ProfileId].Emitters[1].ExtraFields["Heat"].GetInt32() == 42, "custom emitter fields survive the graphical editor");
                    Check(Entities.Collect<AtmosphereProfile>()[ProfileId].Emitters[1].Velocity[1] == .015f, "unrelated edits preserve exact existing velocity");
                    Check(ModFileService.ReadText(Mod, FileName).Contains("AuthorExtension"), "unknown profile fields survive save");
                    step = 10; return;
                case 10:
                    Shot("effects-editor");
                    // The effects editor's own button opens the RPG map editor (rpg_editor, tool id rpg_map_editor).
                    Press(window, "tools/effects_editor/map");
                    step = 2; return;
                case 2:
                    mapWindow = GetTree().Root.FindChildren("*", "", true, false).OfType<RpgEditorWindow>().FirstOrDefault();
                    if (mapWindow == null) throw new InvalidOperationException("the map button did not open the RPG map editor");
                    Check(mapWindow.Visible, "the effects editor's map button opens the RPG map editor");
                    Check(mapWindow.Tool == RpgEditorTool.Effects && mapWindow.EffectProfile == ProfileId,
                        $"the map editor opens on the effects tool with profile {ProfileId} (got {mapWindow.Tool}, {mapWindow.EffectProfile})");
                    mapWindow.OpenMapFile(MapMod, MapPath);
                    Check(mapWindow.Document?.MapId == MapId, $"the RPG map editor opens the map {MapId} of {MapMod}");
                    var editorRenderer = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--editor-renderer=", StringComparison.Ordinal));
                    var projection = editorRenderer == "--editor-renderer=isometric" ? RpgEditorCanvas.Isometric : RpgEditorCanvas.TopDown;
                    mapWindow.SetProjection(projection);
                    Check(mapWindow.Canvas.Projection == projection && mapWindow.Canvas.Scene != null, "the map is drawn in the chosen projection");
                    Shot("map-editor");
                    step++; return;
                case 3:
                    Shot("map-effects");
                    mapWindow.Hide(); window.Hide();
                    map = RpgWorldHost.Current.View.Map; oldMode = map.LightMode;
                    clock = GameData.Instance.Runtime.Time.Tick; clockSaved = true;
                    GameData.Instance.Runtime.Time.SetTime(clock - clock % (24 * 60) + 12 * 60);
                    map.LightMode = "night"; UiLightController.Instance.SetLocationMode("night");
                    UiLightController.Instance.UpdateLightPos(new Vector2(-1000, -1000));
                    RpgWorldHost.Current.View.Present(0);
                    Check(UiLightController.Instance.LampAvailable, "fixed night offers the shared toolbar lamp at noon");
                    step++; return;
                case 4:
                    darkBrightness = Brightness();
                    lamp = GetTree().Root.FindChildren("*", "", true, false).OfType<LampBehaviour>().First();
                    Check(lamp.Visible, "shared lamp icon is visible in the toolbar");
                    var origin = lamp.GetGlobalRect().GetCenter();
                    GetViewport().WarpMouse(origin);
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = origin, GlobalPosition = origin });
                    step++; return;
                case 5:
                    GetViewport().WarpMouse(new Vector2(700, 400));
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(700, 400), GlobalPosition = new Vector2(700, 400), ButtonMask = MouseButtonMask.Left });
                    step++; return;
                case 6:
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(700, 400), GlobalPosition = new Vector2(700, 400) });
                    step++; return;
                case 7:
                    RpgWorldHost.Current.View.Present(0);
                    var mat = RpgWorldHost.Current.View.Presentation.SharedMaterial;
                    Check(mat.GetShaderParameter("time_weights").AsVector4().W == 1, "map uses fixed night while clock is noon");
                    Check(mat.GetShaderParameter("portable_position").AsVector2().DistanceTo(new Vector2(700, 400)) < 3, "dragging the toolbar lamp moves the RPG light to the cursor");
                    Check(mat.GetShaderParameter("portable_radius").AsSingle() == GameData.Instance.Runtime.LampData.Radius, "RPG uses saved lamp radius in pixels");
                    Check(Brightness() > darkBrightness + .01f, "the dragged lamp visibly brightens terrain pixels");
                    Shot("shared-lamp-night");
                    map.LightMode = "day"; UiLightController.Instance.SetLocationMode("day"); RpgWorldHost.Current.View.Present(0);
                    Check(!UiLightController.Instance.LampAvailable && mat.GetShaderParameter("portable_radius").AsSingle() == 0, "fixed day stows and removes the lamp");
                    Check(mat.GetShaderParameter("time_weights").AsVector4().Y == 1, "fixed day reaches the map shader");
                    gallery = new CollectionsGallery { Size = new Vector2(1600, 800), Position = new Vector2(30, 90) };
                    GetTree().Root.AddChild(gallery); gallery.Refresh(); step++; return;
                case 8:
                    var grid = gallery.FindChildren("*", "", true, false).OfType<GridContainer>().First();
                    var cards = grid.GetChildren().OfType<PanelContainer>().ToList();
                    Check(cards.Count >= 3, "collection gallery contains the three games");
                    Check(cards.Take(3).All(c => Mathf.IsEqualApprox(c.Size.X, cards[0].Size.X) && Mathf.IsEqualApprox(c.Size.Y, cards[0].Size.Y)), "collection cells share width and height: " + string.Join(", ", cards.Take(3).Select(c => c.Size.ToString())));
                    var buttons = cards.Take(3).Select(c => c.FindChildren("*", "", true, false).OfType<Button>().First()).ToList();
                    Check(buttons.All(b => Mathf.IsEqualApprox(b.GlobalPosition.Y, buttons[0].GlobalPosition.Y)), "collection action buttons align on the same row");
                    Check(gallery.FindChildren("*", "", true, false).OfType<Control>().Where(c => !string.IsNullOrEmpty(c.TooltipText)).All(c => c.MouseFilter != Control.MouseFilterEnum.Ignore), "collection text tooltips receive the mouse");
                    Shot("collection-gallery"); Finish(); return;
            }
        }
        private float Brightness()
        {
            using var image = GetViewport().GetTexture().GetImage();
            var sum = 0f;
            for (var y = 376; y < 424; y += 6) for (var x = 676; x < 724; x += 6)
            { var pixel = image.GetPixel(x, y); sum += pixel.R + pixel.G; }
            return sum / 128;
        }
        private static T Find<T>(Node root, string name) where T : Node => root.FindChildren(name, "", true, false).OfType<T>().First();
        private static void Press(Node root, string key) => root.FindChildren("*", "", true, false).OfType<Button>().First(b => b.Text == ToolUi.Tr(key)).EmitSignal(BaseButton.SignalName.Pressed);
        private void Check(bool ok, string message) { if (ok) passed++; else failed++; GameLogger.Log("TEST", (ok ? "PASS " : "FAIL ") + message); }
        private void Shot(string name)
        {
            if (shots == null || DisplayServer.GetName() == "headless") return;
            DirAccess.MakeDirRecursiveAbsolute(shots); ViewportCapture.SavePng(GetViewport(), shots + "/" + name + ".png");
        }
        private void Finish()
        {
            step = -1;
            try
            {
            if (window != null) { window.GetParent()?.RemoveChild(window); window.Free(); window = null; }
            if (mapWindow != null) { mapWindow.GetParent()?.RemoveChild(mapWindow); mapWindow.Free(); mapWindow = null; }
            if (gallery != null) { gallery.GetParent()?.RemoveChild(gallery); gallery.Free(); gallery = null; }
            if (diskPath != null) { if (backup == null) { if (File.Exists(diskPath)) File.Delete(diskPath); } else File.WriteAllText(diskPath, backup); }
            if (map != null) map.LightMode = oldMode;
            UiLightController.Instance?.SetLocationMode(oldMode ?? "cycle");
            if (clockSaved) GameData.Instance.Runtime.Time.SetTime(clock);
            }
            catch (Exception ex) { Check(false, "cleanup: " + ex); }
            finally
            {
                GameLogger.Log("TEST", $"[TEST_RESULT] name=effects_editor_edit passed={passed} failed={failed}");
                ApplicationExit.Quit(GetTree(), failed == 0 ? 0 : 1);
            }
        }
    }
}
