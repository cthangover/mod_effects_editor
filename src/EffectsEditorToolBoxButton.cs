namespace Cthangover.Tools.EffectsEditor
{
    using Cthangover.Core.UI.Tool;
    using Godot;
    /// <summary>Exposes the standalone atmosphere editor through the standard in-game tool registry.</summary>
    public class EffectsEditorToolBoxButton : IToolBoxButton
    {
        public string LocaleKey => "tools/effects_editor/title";
        public string ToolId => "effects_editor";
        public string IconPath => "";
        public bool IsVisible() => true;
    }
}
