namespace Cthangover.Tools.EffectsEditor
{
    using System.Collections.Generic;
    using Cthangover.Mods.Interface.Base.Lists;
    using Cthangover.Mods.Effects.Atmosphere;
    using Cthangover.Mods.TileRender;
    using Godot;
    /// <summary>A small projected test surface using the real atmosphere host, with deterministic water regions and no gameplay simulation.</summary>
    public partial class EffectsPreview : ModWidget
    {
        private readonly AtmosphereView view = new();
        private readonly Node2D surface = new();
        private ITileProjection projection;
        private string profile;
        private string renderer = "topdown";
        public bool Paused { get; set; }
        public EffectsPreview() { CustomMinimumSize = new Vector2(420, 400); SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; EnsureConstructed(); }
        protected override void Construct() { MouseFilter = MouseFilterEnum.Ignore; AddChild(surface); surface.AddChild(view); Resized += Rebuild; }
        public void ShowProfile(string id) { profile = id; Rebuild(); }
        public void SetRenderer(string id) { renderer = id; Rebuild(); }
        public void Restart() { view.Release(); Rebuild(); }
        private void Rebuild()
        {
            if (!IsInsideTree()) return;
            projection = TileRendererRegistry.Instance.Resolve(renderer).Projection(32);
            var corners = TileDrawing.CellPolygon(projection, new Rect2(0, 0, 10, 10));
            var bounds = new Rect2(corners[0], Vector2.Zero);
            foreach (var corner in corners) bounds = bounds.Expand(corner);
            var scale = Mathf.Min((Size.X - 32) / bounds.Size.X, (Size.Y - 32) / (bounds.Size.Y + 100));
            scale = Mathf.Max(0.1f, scale);
            surface.Scale = Vector2.One * scale;
            surface.Position = (Size - bounds.Size * scale) / 2 - bounds.Position * scale + new Vector2(0, 40 * scale);
            var water = new List<Vector2>();
            for (var y = 2; y < 8; y++) for (var x = 2; x < 8; x++) water.Add(new Vector2(x, y));
            view.Configure(profile, new AtmosphereContext { Bounds = new Rect2(0, 0, 10, 10), Project = projection.Project,
                Regions = new Dictionary<string, IReadOnlyList<Vector2>> { ["water"] = water, ["painted"] = water } });
            QueueRedraw();
        }
        public override void _Ready() { base._Ready(); Rebuild(); }
        public override void _Process(double delta) { if (!Paused) view.Advance(delta); }
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color("18232d"));
            if (projection == null) return;
            for (var y = 0; y < 10; y++) for (var x = 0; x < 10; x++)
            {
                var points = TileDrawing.CellPolygon(projection, new Rect2(x, y, 1, 1));
                for (var i = 0; i < points.Length; i++) points[i] = surface.Transform * points[i];
                DrawColoredPolygon(points, x >= 2 && x < 8 && y >= 2 && y < 8 ? new Color("284f66") : new Color("354737"));
                for (var i = 0; i < 4; i++) DrawLine(points[i], points[(i + 1) % 4], new Color(1, 1, 1, 0.08f));
            }
        }
    }
}
