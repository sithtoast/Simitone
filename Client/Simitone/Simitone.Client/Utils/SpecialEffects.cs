using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.LotView;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Primitives;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Screens;
using System;
using System.IO;
using System.Linq;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// What the game does with its Special Effect primitive besides centring the camera: TS1's Auto Snapshot and Live
    /// PIP options. It's a graphics layer between the lot and the UI, so a snapshot is the lot without the interface.
    /// - Auto Snapshot: the lot around the object is saved to the family's folder in UserData/PhotoAlbum, named with
    ///   the time and the object's caption (string table 305).
    /// - Live PIP: a small window at the top right shows the object for a few seconds, wherever the camera is. The lot
    ///   is drawn a second time, a few times a second, with the camera moved to the object (2D mode only).
    /// </summary>
    public class SpecialEffects : IGraphicsLayer
    {
        public static SpecialEffects Layer = new SpecialEffects();

        private const float PIPFrameTime = 1 / 6f; //redrawing the lot a second time is slow, so only a few times a second

        private VMEntity SnapshotObject;
        private string SnapshotCaption;
        private int SnapshotSize;
        private bool SnapshotNextFrame;

        private VMEntity PIPObject;
        private float PIPTimeLeft;
        private float PIPFrameIn;
        private RenderTarget2D PIPTarget;
        public Texture2D PIPTexture => PIPObject == null ? null : PIPTarget;
        public Point PIPSize;

        /// <summary>
        /// Called with the VM's TS1SpecialEffect event.
        /// </summary>
        public void Handle(VMSpecialEffectEvent effect)
        {
            if (effect.Object == null) return;
            var settings = GlobalSettings.Default;
            if (effect.Snapshot && settings.TS1AutoSnapshot)
            {
                SnapshotObject = effect.Object;
                SnapshotCaption = effect.Caption;
                SnapshotSize = effect.Operand.Size;
                //after the camera has moved to it (the effect may centre it), and the lot has been drawn
                SnapshotNextFrame = false;
            }
            if (effect.PIP && settings.TS1LivePIP)
            {
                PIPObject = effect.Object;
                //the timeout's units aren't known; a few seconds suits the events that use it
                PIPTimeLeft = Math.Max(4, Math.Min(12, (int)effect.Operand.Timeout));
                PIPSize = (effect.Operand.Size > 1) ? new Point(280, 210) : new Point(200, 150);
                PIPFrameIn = 0;
            }
        }

        public void Initialize(GraphicsDevice device) { }

        public void Update(UpdateState state)
        {
            if (PIPObject == null) return;
            PIPTimeLeft -= 1 / 60f;
            PIPFrameIn -= 1 / 60f;
            if (PIPTimeLeft <= 0 || PIPObject.Dead) PIPObject = null;
        }

        public void PreDraw(GraphicsDevice device)
        {
            var world = (UIScreen.Current as TS1GameScreen)?.vm?.Context.World;
            if (PIPObject == null || world == null || PIPFrameIn > 0) return;
            if (world.State.CameraMode >= CameraRenderMode._3D || PIPObject.WorldUI == null)
            {
                PIPObject = null;
                return;
            }
            PIPFrameIn = PIPFrameTime;
            var width = device.PresentationParameters.BackBufferWidth;
            var height = device.PresentationParameters.BackBufferHeight;
            if (PIPTarget == null || PIPTarget.Width != width || PIPTarget.Height != height)
            {
                PIPTarget?.Dispose();
                PIPTarget = new RenderTarget2D(device, width, height);
            }
            var oldCenter = world.State.CenterTile;
            try
            {
                world.State.CenterTile = world.State.Project2DCenterTile(PIPObject.WorldUI.Position);
                world.PreDraw(device);
                device.SetRenderTarget(PIPTarget);
                device.Clear(Color.Black);
                world.Draw(device);
            }
            finally
            {
                device.SetRenderTarget(null);
                world.State.CenterTile = oldCenter;
            }
        }

        public void Draw(GraphicsDevice device)
        {
            if (SnapshotObject == null) return;
            //wait a frame, so the lot is drawn where the effect moved the camera
            if (!SnapshotNextFrame)
            {
                SnapshotNextFrame = true;
                return;
            }
            var game = UIScreen.Current as TS1GameScreen;
            try
            {
                if (game?.vm != null) SaveSnapshot(device, game);
            }
            catch (Exception) { }
            SnapshotObject = null;
        }

        private void SaveSnapshot(GraphicsDevice device, TS1GameScreen game)
        {
            var world = game.vm.Context.World;
            var width = device.PresentationParameters.BackBufferWidth;
            var height = device.PresentationParameters.BackBufferHeight;
            //the object's place on screen: the centre if the camera is on it
            var centre = new Vector2(width, height) / 2;
            if (world.State.CameraMode < CameraRenderMode._3D && SnapshotObject.WorldUI != null)
            {
                var space = world.State.WorldSpace;
                var offset = space.GetScreenFromTile(new Vector3(world.State.Project2DCenterTile(SnapshotObject.WorldUI.Position), 0))
                    - space.GetScreenFromTile(new Vector3(world.State.CenterTile, 0));
                centre += offset * world.State.PreciseZoom * (width / space.WorldPxWidth);
            }
            var size = (SnapshotSize > 1) ? new Point(480, 360) : new Point(320, 240);
            size = new Point(Math.Min(size.X, width), Math.Min(size.Y, height));
            var rect = new Rectangle((int)centre.X - size.X / 2, (int)centre.Y - size.Y / 2, size.X, size.Y);
            rect.X = Math.Max(0, Math.Min(width - size.X, rect.X));
            rect.Y = Math.Max(0, Math.Min(height - size.Y, rect.Y));

            var data = new Color[size.X * size.Y];
            device.GetBackBufferData(rect, data, 0, data.Length);
            for (int i = 0; i < data.Length; i++) data[i].A = 255;

            var family = game.vm.TS1State.CurrentFamily;
            var familyName = (family == null) ? "Snapshots" : (FSO.Content.Content.Get().Neighborhood.GetFamilyString(family.ChunkID)?.GetString(0) ?? "Family " + family.ChunkID);
            var folder = Path.Combine(FSO.Content.Content.Get().Neighborhood.UserPath, "PhotoAlbum", Clean(familyName));
            Directory.CreateDirectory(folder);
            var name = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + (SnapshotCaption == "" ? "" : " " + Clean(SnapshotCaption));
            using (var tex = new Texture2D(device, size.X, size.Y))
            using (var file = File.Create(Path.Combine(folder, name + ".png")))
            {
                tex.SetData(data);
                tex.SaveAsPng(file, size.X, size.Y);
            }
        }

        private static string Clean(string name)
        {
            var bad = Path.GetInvalidFileNameChars().Concat(new[] { ':', '/', '\\', '?', '*', '"', '<', '>', '|' }).ToArray();
            return new string(name.Select(x => bad.Contains(x) ? '_' : x).ToArray()).Trim();
        }
    }

    /// <summary>
    /// Live PIP's window: the middle of the lot drawn around the event, framed, at the top right of the screen.
    /// </summary>
    public class UIPictureInPicture : UIElement
    {
        public override void Draw(UISpriteBatch batch)
        {
            var effects = SpecialEffects.Layer;
            var tex = effects.PIPTexture;
            if (tex == null) return;
            var size = effects.PIPSize;
            var screen = UIScreen.Current;
            var pos = new Vector2(screen.ScreenWidth - size.X - 12, 12);
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            //the classic panels' navy, with a light inner edge
            DrawLocalTexture(batch, px, null, pos - new Vector2(4), new Vector2(size.X + 8, size.Y + 8), new Color(16, 24, 72));
            DrawLocalTexture(batch, px, null, pos - new Vector2(1), new Vector2(size.X + 2, size.Y + 2), new Color(120, 140, 200));
            var source = new Rectangle((tex.Width - size.X) / 2, (tex.Height - size.Y) / 2, size.X, size.Y);
            DrawLocalTexture(batch, tex, source, pos, Vector2.One, Color.White);
        }
    }
}
