namespace Cthangover.Tools.EffectsEditor.Tests
{
    using System.Linq;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Utils;
    public class EffectsEditorTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        // Only its own flag: --with-test-suites (the atlases) mounts every suite, and this driver quits the process.
        public void OnModResourcesReady() { if (queued || !Godot.OS.GetCmdlineUserArgs().Contains("--itest=effects_editor_edit")) return; queued = true; ModRoot.AddDeferred(new EffectsEditorTestDriver()); }
    }
}
