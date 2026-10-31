namespace Cthangover.Tools.EffectsEditor
{
    using Cthangover.Core.UI.Tool;
    using Godot;
    /// <summary>Exposes the standalone atmosphere editor through the standard in-game tool registry.</summary>
    public class EffectsEditorToolProvider : IToolProvider
    {
        public string LocaleKey => "tools/effects_editor/title";
        public string Id => "effects_editor";
        public Window CreateWindow() => new EffectsEditorWindow();
    }
}
