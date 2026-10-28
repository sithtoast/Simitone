using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.HIT;
using FSO.HIT.Model;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// The classic Options panel: Save, Neighborhood and Quit, and the Play, Sound and Graphics option pages. The
    /// positions and names are the game's own, from its options string table (145). Hovering an option explains it in a
    /// popup above the strip, with the game's picture where it has one.
    /// </summary>
    public class UIClassicOptionsPanel : UIClassicStrip
    {
        private const int SettingsX = 275; //where the game puts the first column of options
        private UIButton PlayButton;
        private UIButton SoundButton;
        private UIButton GraphicsButton;
        private UIContainer Page;
        private UIClassicInfoPopup Popup;

        public UIClassicOptionsPanel(TS1GameScreen game) : base(game)
        {
            string str(int i) => GameFacade.Strings.GetString("145", i.ToString());
            UIButton btn(string name, int x, int y, string tooltip, Action click)
            {
                var b = new UIButton(Tex("Buttons/" + name)) { Position = new Vector2(x, y), Tooltip = tooltip };
                b.OnButtonClick += (e) => click();
                Add(b);
                return b;
            }
            btn("OptSave.BMP", 29, 6, str(3), () => Game.Save());
            btn("OptNbhd.bmp", 88, 6, str(1), () => Game.ReturnToNeighbourhood());
            btn("OptExit.bmp", 159, 6, str(5), () => Game.CloseAttempt());
            GraphicsButton = btn("optgraphics.bmp", 29, 58, str(7), () => { });
            GraphicsButton.Disabled = true; //Simitone's graphics settings aren't adjustable in game here
            SoundButton = btn("optsound.bmp", 89, 58, str(9), () => ShowPage(1));
            PlayButton = btn("optplay.bmp", 151, 58, str(11), () => ShowPage(0));

            Add(new UIImage(Tex("Backgrounds/OptionsToothpkHoriz.bmp")) { Position = new Vector2(14, 50) });
            Add(new UIImage(Tex("Backgrounds/OptionsToothpkVert.bmp")) { Position = new Vector2(226, 6) });

            Popup = new UIClassicInfoPopup(-4) { X = 8 };
            ShowPage(0);
            Add(Popup);
            GameResized();
        }

        private void ShowPage(int page)
        {
            PlayButton.Selected = page == 0;
            SoundButton.Selected = page == 1;
            if (Page != null) Remove(Page);
            Popup?.Hide();
            Page = (page == 0) ? PlayPage() : SoundPage();
            Add(Page);
            if (Popup != null) Add(Popup); //keep it on top
        }

        /// <summary>
        /// Shows the popup while the mouse is over an option.
        /// </summary>
        private Action<bool> Explains(string picture, string title, string text)
        {
            return (hover) =>
            {
                if (hover) Popup.Show((picture == null) ? null : Tex(picture), title, text);
                else Popup.Hide();
            };
        }

        private UIContainer PlayPage()
        {
            string str(int i) => GameFacade.Strings.GetString("145", i.ToString());
            var page = new UIContainer();
            var y = 9;
            void check(string name, string picture, string title, string text, bool value, Action<bool> set)
            {
                var box = new UIClassicCheckbox(name, Style(10), 190) { Position = new Vector2(SettingsX, y), Checked = value };
                box.OnToggle += (on) =>
                {
                    set(on);
                    GlobalSettings.Default.Save();
                };
                box.OnHover += Explains(picture, title, text);
                page.Add(box);
                y += 20; //the game's spacing
            }
            check(str(59), "PopupOptFreeWill.bmp", str(60), str(61), VM.FreeWillEnabled, (on) =>
            {
                VM.FreeWillEnabled = on;
                GlobalSettings.Default.TS1FreeWill = on;
            });
            check(str(62), "PopupOptEdgeScroll.bmp", str(63), str(64), GlobalSettings.Default.EdgeScroll, (on) => GlobalSettings.Default.EdgeScroll = on);
            check("Classic UI", null, "About Classic UI",
                "Draws the control panel, panels and dialogs with the original art from your copy of The Sims. Turn it off to use Simitone's own interface.",
                true, (on) =>
                {
                    GlobalSettings.Default.TS1ClassicUI = on;
                    Game.Frontend.RebuildDesktopUCP();
                });
            return page;
        }

        private UIContainer SoundPage()
        {
            string str(int i) => GameFacade.Strings.GetString("145", i.ToString());
            var page = new UIContainer();
            var y = 13;
            void slider(int name, string picture, HITVolumeGroup group, Func<byte> get, Action<byte> set)
            {
                page.Add(new UILabel()
                {
                    Caption = str(name),
                    CaptionStyle = Style(10),
                    Alignment = TextAlignment.Right | TextAlignment.Middle,
                    Position = new Vector2(235, y - 3),
                    Size = new Vector2(60, 20)
                });
                var s = new UIClassicSlider(130) { Position = new Vector2(305, y), Value = get() };
                s.OnChange += (v) =>
                {
                    set((byte)v);
                    HITVM.Get().SetMasterVolume(group, v / 10f);
                    GlobalSettings.Default.Save();
                };
                s.OnHover += Explains(picture, str(name + 1), str(name + 2));
                page.Add(s);
                y += 28;
            }
            slider(44, "PopupOptSFX.bmp", HITVolumeGroup.FX, () => GlobalSettings.Default.FXVolume, (v) => GlobalSettings.Default.FXVolume = v);
            slider(47, "PopupOptMusic.bmp", HITVolumeGroup.MUSIC, () => GlobalSettings.Default.MusicVolume, (v) => GlobalSettings.Default.MusicVolume = v);
            slider(50, "PopupOptVox.bmp", HITVolumeGroup.VOX, () => GlobalSettings.Default.VoxVolume, (v) => GlobalSettings.Default.VoxVolume = v);
            return page;
        }
    }
}
