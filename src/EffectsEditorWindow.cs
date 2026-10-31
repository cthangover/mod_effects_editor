namespace Cthangover.Tools.EffectsEditor
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Effects.Atmosphere;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Interface.Theming;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.Services;
    using Cthangover.Tools.EntityEditor;
    using Godot;

    /// <summary>Owns unsaved profile previews and an EntityDocument. Only Save writes files; closing removes every preview override.</summary>
    public partial class EffectsEditorWindow : ToolWindow
    {
        private readonly List<string> mods = new();
        private readonly HashSet<string> previewIds = new();
        private readonly Dictionary<string, SpinBox> numbers = new();
        private readonly Dictionary<string, double> displayedNumbers = new();
        private readonly OptionButton modPicker = new() { Name = "EffectMod" };
        private readonly ItemList profiles = new() { Name = "EffectProfiles", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        private readonly ItemList emitters = new() { Name = "EffectEmitters", CustomMinimumSize = new Vector2(0, 130) };
        private readonly LineEdit idEdit = new() { Name = "EffectId" };
        private readonly LineEdit nameEdit = new() { Name = "EffectName" };
        private readonly OptionButton typePicker = new() { Name = "EmitterType" };
        private readonly OptionButton shapePicker = new() { Name = "EmitterShape" };
        private readonly ColorPickerButton color = new() { Name = "EmitterColor", EditAlpha = true };
        private readonly LineEdit region = new() { Name = "EmitterRegion" };
        private readonly CodeEdit extra = ToolUi.Code();
        private readonly EffectsPreview preview = new() { Name = "EffectPreview" };
        private VBoxContainer inspector;
        private EntityDocument document;
        private EntityRecord selected;
        private string modId;
        private bool suppress;
        private int emitterIndex = -1;
        private bool invalidForm;
        public EntityDocument Document => document;
        public string SelectedId => selected?.Id;

        public EffectsEditorWindow() : base("tools/effects_editor/title")
        {
            BuildUi();
            mods.AddRange(ModFileService.ListWritableMods());
            foreach (var id in mods) modPicker.AddItem(id);
            var initial = mods.FindIndex(m => ModFileService.ListFiles(m, "atmospheres", ".json").Count > 0);
            if (initial < 0) initial = 0;
            if (mods.Count > 0) { modPicker.Select(initial); OpenMod(mods[initial]); }
            SetHint(ToolUi.Tr("tools/effects_editor/hint"));
        }
        private void BuildUi()
        {
            var outer = CreateFillContainer(); AddChild(outer);
            var bar = CreateToolbar(); outer.AddChild(bar);
            bar.AddChild(modPicker); modPicker.ItemSelected += OnModPicked;
            bar.AddChild(ToolUi.Button("tools/common/save", OnSavePressed, ButtonKind.Primary));
            bar.AddChild(ToolUi.Button("tools/entity_editor/new", AddNew));
            bar.AddChild(ToolUi.Button("tools/entity_editor/duplicate", DuplicateProfile));
            bar.AddChild(ToolUi.Button("tools/entity_editor/delete", DeleteProfile));
            bar.AddChild(ToolUi.Button("tools/entity_editor/undo", Undo));
            bar.AddChild(ToolUi.Button("tools/entity_editor/redo", Redo));
            bar.AddChild(ToolUi.Button("tools/effects_editor/map", OpenMap));
            var split = new HSplitContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; outer.AddChild(split);
            var left = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            profiles.CustomMinimumSize = new Vector2(0, 200); left.AddChild(profiles); profiles.ItemSelected += PickProfile;
            split.AddChild(CreateSidebar(left, 240));
            var center = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            split.AddChild(center);
            var transport = new HBoxContainer(); center.AddChild(transport);
            var pause = new CheckBox { Text = ToolUi.Tr("tools/effects_editor/pause") }; pause.Toggled += Pause; transport.AddChild(pause);
            transport.AddChild(ToolUi.Button("tools/effects_editor/restart", preview.Restart));
            var projection = new OptionButton { Name = "EffectProjection" };
            foreach (var id in Cthangover.Mods.TileRender.TileRendererRegistry.Instance.Ids) { projection.AddItem(id); projection.SetItemMetadata(projection.ItemCount - 1, id); if (id == "topdown") projection.Select(projection.ItemCount - 1); }
            projection.ItemSelected += index => preview.SetRenderer(projection.GetItemMetadata((int)index).AsString()); transport.AddChild(projection);
            center.AddChild(preview);
            var form = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            inspector = form;
            split.AddChild(CreateSidebar(form, 390));
            ToolUi.Field(form, "tools/entity_editor/id", idEdit); idEdit.TextChanged += ProfileChanged;
            ToolUi.Field(form, "tools/effects_editor/name", nameEdit); nameEdit.TextChanged += ProfileChanged;
            form.AddChild(emitters); emitters.ItemSelected += PickEmitter;
            var add = new HFlowContainer(); form.AddChild(add);
            foreach (var preset in new[] { "flake", "leaf", "glow", "dust", "streak", "ripples" })
            {
                var key = preset;
                add.AddChild(ToolUi.Button("tools/effects_editor/" + key, () => AddEmitter(key)));
            }
            add.AddChild(ToolUi.Button("tools/effects_editor/copy_emitter", CopyEmitter));
            add.AddChild(ToolUi.Button("tools/effects_editor/remove_emitter", RemoveEmitter));
            foreach (var id in AtmosphereEmitterRegistry.Ids) { typePicker.AddItem(id == "particles" ? ToolUi.Tr("tools/effects_editor/particles") : id == "ripples" ? ToolUi.Tr("tools/effects_editor/ripples") : id); typePicker.SetItemMetadata(typePicker.ItemCount - 1, id); }
            ToolUi.Field(form, "tools/effects_editor/type", typePicker); typePicker.ItemSelected += TypeChanged;
            foreach (var shape in new[] { "flake", "leaf", "glow", "dust", "streak" }) { shapePicker.AddItem(ToolUi.Tr("tools/effects_editor/" + shape)); shapePicker.SetItemMetadata(shapePicker.ItemCount - 1, shape); }
            ToolUi.Field(form, "tools/effects_editor/shape", shapePicker); shapePicker.ItemSelected += EmitterChoiceChanged;
            ToolUi.Field(form, "tools/effects_editor/color", color); color.ColorChanged += ColorChanged;
            Number(form, "Count", 0, 512, 1); Number(form, "Seed", -100000, 100000, 1);
            foreach (var axis in new[] { "X", "Y", "Z" }) Number(form, "Velocity" + axis, -20, 20, 0.001);
            Number(form, "HeightMin", 0, 32, 0.05); Number(form, "HeightMax", 0.05, 64, 0.05);
            Number(form, "SizeMin", 0.05, 32, 0.05); Number(form, "SizeMax", 0.05, 32, 0.05);
            Number(form, "Sway", -10, 10, 0.01); Number(form, "Pulse", 0, 1, 0.01); Number(form, "Trail", 0, 5, 0.01);
            Number(form, "CycleSeconds", 0.05, 60, 0.05); Number(form, "RadiusMin", 0, 32, 0.01); Number(form, "RadiusMax", 0, 32, 0.01); Number(form, "Segments", 3, 128, 1);
            ToolUi.Field(form, "tools/effects_editor/region", region); region.TextChanged += EmitterTextChanged;
            extra.Name = "EmitterExtra"; extra.CustomMinimumSize = new Vector2(0, 90);
            ToolUi.Field(form, "tools/effects_editor/extra", extra); extra.TextChanged += EmitterChanged;
            foreach (var row in form.GetChildren().OfType<HBoxContainer>())
                if (row.GetChildCount() > 0 && row.GetChild(0) is Label label) { label.CustomMinimumSize = new Vector2(190, 0); label.TooltipText = label.Text; }
        }
        private void Number(VBoxContainer form, string key, double min, double max, double step)
        {
            var control = ToolUi.Number(min, max, step, NumberChanged); control.Name = "Emitter" + key;
            if (key.StartsWith("Velocity") || key == "Sway" || key == "Seed") { control.AllowGreater = true; control.AllowLesser = true; }
            if (key.StartsWith("Height") || key is "CycleSeconds" or "Trail" or "Pulse") control.AllowGreater = true;
            numbers[key] = ToolUi.Field(form, "tools/effects_editor/" + key, control);
        }
        private void Pause(bool value) => preview.Paused = value;
        private void OpenMap()
        {
            var tool = Cthangover.Core.UI.Tool.ToolFactory.Instance.Get("rpg_map_editor");
            if (tool == null) { SetStatus(ToolUi.Tr("tools/effects_editor/map_missing"), ToolStatusKind.Error); return; }
            var window = tool.CreateWindow();
            ToolWindow.ShowWindow(window);
            // Plan 14.G10: the map editor opens on its Effects tool with this profile chosen, to paint its mask.
            (window as IAtmosphereMaskEditor)?.ShowMaskTool(selected?.Id);
        }
        private void OnModPicked(long index) { var wanted = mods[(int)index]; RunDiscardingChanges(() => OpenMod(wanted), RestoreModPicker); }
        private void RestoreModPicker() => modPicker.Select(mods.IndexOf(modId));
        public void OpenMod(string id)
        {
            ClearPreviews(); modId = id;
            var modIndex = mods.IndexOf(id); if (modIndex >= 0) modPicker.Select(modIndex);
            var sources = ModFileService.ListFiles(id, "", ".json").Select(p => (p, ModFileService.ReadText(id, p)));
            document = EntityDocument.Load("atmosphere", "atmospheres/" + id + ".json", sources, out var skipped);
            selected = document.Records.FirstOrDefault(); MarkClean(); RefreshProfiles();
            if (skipped.Count > 0) SetStatus(string.Join(", ", skipped), ToolStatusKind.Error);
        }
        private void RefreshProfiles()
        {
            suppress = true; profiles.Clear();
            foreach (var record in document.Records) { profiles.AddItem(record.Id); if (record == selected) profiles.Select(profiles.ItemCount - 1); }
            suppress = false; BindProfile();
        }
        private void PickProfile(long index) { selected = document.Records[(int)index]; BindProfile(); }
        private JsonArray EmitterArray => selected?.Data["Emitters"] as JsonArray;
        private void BindProfile()
        {
            invalidForm = false;
            inspector.Visible = selected != null;
            suppress = true;
            idEdit.Text = selected?.Id ?? ""; nameEdit.Text = selected?.Data["Name"]?.ToString() ?? "";
            emitters.Clear();
            if (EmitterArray != null) foreach (var emitter in EmitterArray) emitters.AddItem(EmitterLabel(emitter));
            emitterIndex = emitters.ItemCount > 0 ? Math.Clamp(emitterIndex, 0, emitters.ItemCount - 1) : -1;
            if (emitterIndex >= 0) emitters.Select(emitterIndex);
            suppress = false; BindEmitter(); PublishPreview();
        }
        private static string EmitterLabel(JsonNode emitter)
        {
            var type = emitter?["Type"]?.ToString() ?? "particles";
            if (type == "ripples") return ToolUi.Tr("tools/effects_editor/ripples");
            if (type == "particles") return ToolUi.Tr("tools/effects_editor/" + (emitter?["Shape"]?.ToString() ?? "flake"));
            return type;
        }
        private void PickEmitter(long index) { emitterIndex = (int)index; BindEmitter(); }
        private AtmosphereEmitterInfo ReadEmitter() => EmitterArray != null && emitterIndex >= 0 && emitterIndex < EmitterArray.Count
            ? EmitterArray[emitterIndex].Deserialize<AtmosphereEmitterInfo>(JsonOptionsProvider.ReadOptions) : null;
        private void BindEmitter()
        {
            suppress = true;
            try
            {
                var info = ReadEmitter();
                if (info == null) return;
                SelectValue(typePicker, info.Type); SelectValue(shapePicker, info.Shape);
                color.Color = Color.HtmlIsValid(info.Color) ? new Color(info.Color) : Colors.White;
                numbers["Count"].Value = info.Count; numbers["Seed"].Value = info.Seed;
                var axes = new[] { "X", "Y", "Z" };
                for (var i = 0; i < 3; i++) numbers["Velocity" + axes[i]].Value = info.Velocity[i];
                numbers["HeightMin"].Value = info.Height[0]; numbers["HeightMax"].Value = info.Height[1];
                numbers["SizeMin"].Value = info.Size[0]; numbers["SizeMax"].Value = info.Size[1];
                numbers["Sway"].Value = info.Sway; numbers["Pulse"].Value = info.Pulse; numbers["Trail"].Value = info.Trail;
                numbers["CycleSeconds"].Value = info.CycleSeconds; numbers["RadiusMin"].Value = info.RadiusMin; numbers["RadiusMax"].Value = info.RadiusMax; numbers["Segments"].Value = info.Segments;
                region.Text = info.Region; extra.Text = JsonSerializer.Serialize(info.ExtraFields ?? new(), JsonOptionsProvider.WriteIndentedIgnoreNullOptions);
            }
            catch (Exception ex) { invalidForm = true; SetStatus(ex.Message, ToolStatusKind.Error); }
            finally { RememberNumbers(); suppress = false; UpdateFieldVisibility(); }
        }
        private void UpdateFieldVisibility()
        {
            var ripple = typePicker.Selected >= 0 && typePicker.GetItemMetadata(typePicker.Selected).AsString() == "ripples";
            foreach (var pair in numbers)
            {
                var ringField = pair.Key is "CycleSeconds" or "RadiusMin" or "RadiusMax" or "Segments";
                var common = pair.Key is "Count" or "Seed";
                ((Control)pair.Value.GetParent()).Visible = common || (ripple ? ringField : !ringField);
            }
            ((Control)shapePicker.GetParent()).Visible = !ripple;
            ((Control)region.GetParent()).Visible = ripple;
        }
        private static void SelectValue(OptionButton picker, string value)
        {
            for (var i = 0; i < picker.ItemCount; i++) if (picker.GetItemMetadata(i).AsString() == value) { picker.Select(i); return; }
            picker.AddItem(value); picker.SetItemMetadata(picker.ItemCount - 1, value); picker.Select(picker.ItemCount - 1);
        }
        private void ProfileChanged(string value)
        {
            if (suppress || selected == null) return;
            document.BeginChange(); var previous = selected.Id;
            selected.Data["Id"] = idEdit.Text.Trim(); selected.Data["Name"] = nameEdit.Text;
            if (previous != selected.Id) { AtmosphereCatalog.RemovePreview(previous); previewIds.Remove(previous); }
            SetDirty(); PublishPreview();
        }
        private void TypeChanged(long index) { EmitterChanged(); UpdateFieldVisibility(); }
        private void EmitterChoiceChanged(long index) { EmitterChanged(); UpdateFieldVisibility(); }
        private void EmitterTextChanged(string value) => EmitterChanged();
        private void NumberChanged(double value) => EmitterChanged();
        private void ColorChanged(Color value) => EmitterChanged();
        private float N(string key) => (float)numbers[key].Value;
        // SpinBox display precision must never quantize an untouched source setting during unrelated edits.
        private float V(string key, float previous) => displayedNumbers.TryGetValue(key, out var displayed) && numbers[key].Value == displayed ? previous : N(key);
        private int I(string key, int previous) => displayedNumbers.TryGetValue(key, out var displayed) && numbers[key].Value == displayed ? previous : checked((int)numbers[key].Value);
        private void RememberNumbers() { foreach (var pair in numbers) displayedNumbers[pair.Key] = pair.Value.Value; }
        private void EmitterChanged()
        {
            if (suppress || selected == null || emitterIndex < 0) return;
            try
            {
                var info = ReadEmitter(); if (info == null) return;
                info.Type = typePicker.GetItemMetadata(typePicker.Selected).AsString(); info.Shape = shapePicker.GetItemMetadata(shapePicker.Selected).AsString();
                info.Count = I("Count", info.Count); info.Seed = I("Seed", info.Seed); info.Color = "#" + color.Color.ToHtml();
                info.Velocity = new[] { V("VelocityX", info.Velocity[0]), V("VelocityY", info.Velocity[1]), V("VelocityZ", info.Velocity[2]) };
                info.Height = new[] { V("HeightMin", info.Height[0]), V("HeightMax", info.Height[1]) }; info.Size = new[] { V("SizeMin", info.Size[0]), V("SizeMax", info.Size[1]) };
                info.Sway = V("Sway", info.Sway); info.Pulse = V("Pulse", info.Pulse); info.Trail = V("Trail", info.Trail);
                info.CycleSeconds = V("CycleSeconds", info.CycleSeconds); info.RadiusMin = V("RadiusMin", info.RadiusMin); info.RadiusMax = V("RadiusMax", info.RadiusMax); info.Segments = I("Segments", info.Segments); info.Region = region.Text;
                info.ExtraFields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(extra.Text);
                // Invalid intermediate input stays in the form; the last valid preview remains alive.
                AtmosphereValidation.Validate(info);
                invalidForm = false;
                document.BeginChange(); EmitterArray[emitterIndex] = JsonSerializer.SerializeToNode(info);
                RememberNumbers();
                SetDirty(); PublishPreview();
            }
            catch (Exception ex) { invalidForm = true; SetStatus(ex.Message, ToolStatusKind.Error); }
        }
        private void PublishPreview()
        {
            if (selected == null) { preview.ShowProfile(null); return; }
            try
            {
                var profile = selected.Data.Deserialize<AtmosphereProfile>(JsonOptionsProvider.ReadOptions);
                AtmosphereValidation.Validate(profile); invalidForm = false; AtmosphereCatalog.Preview(profile); previewIds.Add(profile.ID); preview.ShowProfile(profile.ID);
                SetStatus(ToolUi.Tr("tools/effects_editor/live"), ToolStatusKind.Ok);
            }
            catch (Exception ex) { invalidForm = true; SetStatus(ex.Message, ToolStatusKind.Error); }
        }
        public void AddNew()
        {
            if (document == null) return;
            document.BeginChange();
            selected = document.Add(new JsonObject { ["Id"] = document.FreeId(modId + "/effect"), ["Emitters"] = JsonSerializer.SerializeToNode(new[] { new AtmosphereEmitterInfo() }) });
            emitterIndex = 0; SetDirty(); RefreshProfiles();
        }
        private void DuplicateProfile()
        {
            if (selected == null) return;
            document.BeginChange(); selected = document.Duplicate(selected, document.FreeId(selected.Id + "_copy")); SetDirty(); RefreshProfiles();
        }
        private void DeleteProfile()
        {
            if (selected == null) return;
            document.BeginChange(); AtmosphereCatalog.RemovePreview(selected.Id); previewIds.Remove(selected.Id);
            document.Remove(selected); selected = document.Records.FirstOrDefault(); SetDirty(); RefreshProfiles();
        }
        private void AddEmitter(string preset)
        {
            if (selected == null) return;
            document.BeginChange(); selected.Data["Emitters"] ??= new JsonArray();
            var info = new AtmosphereEmitterInfo { Shape = preset == "ripples" ? "flake" : preset, Type = preset == "ripples" ? "ripples" : "particles" };
            if (preset == "glow") { info.Velocity = new[] { 0.01f, 0.015f, 0f }; info.Pulse = 0.9f; info.Color = "#cbe785a0"; }
            if (preset == "streak") { info.Velocity = new[] { 0.1f, 0.02f, -3f }; info.Trail = 0.12f; }
            EmitterArray.Add(JsonSerializer.SerializeToNode(info)); emitterIndex = EmitterArray.Count - 1; SetDirty(); BindProfile();
        }
        private void CopyEmitter()
        {
            if (emitterIndex < 0) return;
            document.BeginChange(); EmitterArray.Add(EmitterArray[emitterIndex].DeepClone()); emitterIndex = EmitterArray.Count - 1; SetDirty(); BindProfile();
        }
        private void RemoveEmitter()
        {
            if (emitterIndex < 0) return;
            document.BeginChange(); EmitterArray.RemoveAt(emitterIndex); SetDirty(); BindProfile();
        }
        private void Undo() => History(false);
        private void Redo() => History(true);
        private void History(bool redo)
        {
            if (document == null) return;
            var id = selected?.Id;
            if (!(redo ? document.Redo() : document.Undo())) return;
            ClearPreviews(); selected = document.Find(id) ?? document.Records.FirstOrDefault(); SetDirty(); RefreshProfiles();
        }
        protected override void OnSaveShortcut() => Save();
        private void OnSavePressed() => Save();
        public bool Save()
        {
            if (document == null) return false;
            try
            {
                if (invalidForm) throw new ArgumentException(ToolUi.Tr("tools/effects_editor/invalid"));
                var errors = document.Validate();
                if (errors.Count > 0) throw new ArgumentException(string.Format(ToolUi.Tr(errors[0].Key), errors[0].Argument));
                foreach (var record in document.Records) AtmosphereValidation.Validate(record.Data.Deserialize<AtmosphereProfile>(JsonOptionsProvider.ReadOptions));
                foreach (var (path, text) in document.ChangedFiles()) if (!ModFileService.WriteText(modId, path, text)) throw new InvalidOperationException(ToolUi.Tr("tools/common/readonly"));
                document.MarkSaved(); MarkClean();
                ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache(); ModManager.Instance.GetResolver<FileListResolver>()?.ClearCache();
                ClearPreviews(); PublishPreview();
                SetStatus(ToolUi.Tr("tools/effects_editor/saved"), ToolStatusKind.Ok); return true;
            }
            catch (Exception ex) { SetStatus(ex.Message, ToolStatusKind.Error); return false; }
        }
        private void ClearPreviews() { foreach (var id in previewIds) AtmosphereCatalog.RemovePreview(id); previewIds.Clear(); AtmosphereCatalog.Refresh(); }
        protected override void Cleanup() { ClearPreviews(); base.Cleanup(); }
        protected override bool HandleKey(InputEventKey key)
        {
            if (key.CtrlPressed && key.Keycode == Key.Z) { if (key.ShiftPressed) Redo(); else Undo(); return true; }
            if (key.CtrlPressed && key.Keycode == Key.Y) { Redo(); return true; }
            return false;
        }
    }
}
