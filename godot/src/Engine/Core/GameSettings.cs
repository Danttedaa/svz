using System;
using System.Reflection;
using Godot;

namespace Svz
{
    /// Audio settings and a modal menu, separate from gameplay saves and volume fades.
    /// No graphic controls or additional hold gestures.
    public static class GameSettings
    {
        public const string ConfigPath = "user://svz_audio.cfg";
        public const string MasterBus = "SvZ Audio", MusicBus = "SvZ Music", EffectsBus = "SvZ Effects";
        public static double Master { get; private set; } = 100;
        public static double Music { get; private set; } = 100;
        public static double Effects { get; private set; } = 100;
        public static bool Muted { get; private set; }
        public static bool IsOpen { get; private set; }
        private static bool LoggedMusic, LoggedEffects;
        private static bool Ready, WasPaused, WasQuitOnBack, EntryWasVisible, PauseContext, EntryClosing, IgnoreClosingReleases;
        private static Node Root;
        private static CanvasLayer Layer;
        private static Control Ui, Modal;
        private static Button PauseEntry;
        private static Label SaveStatus;
        private static Action ClearInput;
        private static double Scale = 1;
        private static bool BackRestorePending;
        private static int BackRestoreToken;

        public static double ClampVolume(double value, double fallback = 100) =>
            double.IsFinite(value) ? Math.Clamp(value, 0, 100) : fallback;
        public static bool IsMusic(string clipName) => clipName != null && clipName.StartsWith("BGM_", StringComparison.OrdinalIgnoreCase);

        public static void Init(Node root)
        {
            Root = root;
            Load(); EnsureBuses(); ApplyAudio();
            // Bind only the port's explicit bridge methods, once, not during audio playback.
            var assembly = root.GetType().Assembly;
            var reset = assembly.GetType("Svz.InputState")?.GetMethod("ResetForSettings", BindingFlags.Public | BindingFlags.Static);
            ClearInput = reset == null ? null : (Action)reset.CreateDelegate(typeof(Action));
            BuildMenu();
            root.GetViewport().SizeChanged += Layout;
            Layout();
            // Diagnostic capture: --test-audio-settings[=seconds] opens the modal after that delay
            // (default 2 s), saves user://settings-preview.png and quits. No gesture is involved.
            double previewDelay = -1;
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg == "--test-audio-settings") previewDelay = 2;
                else if (arg.StartsWith("--test-audio-settings=") && double.TryParse(arg.Substring(22),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                    previewDelay = Math.Clamp(seconds, 0, 600);
            if (previewDelay >= 0)
            {
                double remaining = previewDelay;
                Action<double, int> preview = null;
                preview = (delta, _) => {
                    if ((remaining -= delta) > 0) return;
                    FramePacing.AfterFrame -= preview;
                    Open();
                    root.GetTree().CreateTimer(2).Timeout += () => {
                        using var frame = root.GetViewport().GetTexture().GetImage();
                        frame.SavePng("user://settings-preview.png");
                        root.GetTree().Quit();
                    };
                };
                FramePacing.AfterFrame += preview;
            }
        }

        public static void Load()
        {
            var cfg = new ConfigFile();
            Master = Music = Effects = 100; Muted = false;
            if (cfg.Load(ConfigPath) != Error.Ok) return;
            Master = ReadVolume(cfg, "master"); Music = ReadVolume(cfg, "music"); Effects = ReadVolume(cfg, "effects");
            if (cfg.HasSectionKey("audio", "mute"))
            {
                var v = cfg.GetValue("audio", "mute");
                if (v.VariantType == Variant.Type.Bool) Muted = v.AsBool();
            }
        }
        private static double ReadVolume(ConfigFile cfg, string key)
        {
            var v = cfg.GetValue("audio", key, 100);
            return v.VariantType == Variant.Type.Int || v.VariantType == Variant.Type.Float ? ClampVolume(v.AsDouble()) : 100;
        }
        public static Error Save()
        {
            var cfg = new ConfigFile();
            cfg.SetValue("audio", "master", Master); cfg.SetValue("audio", "music", Music);
            cfg.SetValue("audio", "effects", Effects); cfg.SetValue("audio", "mute", Muted);
            var result = cfg.Save(ConfigPath);
            if (SaveStatus != null) SaveStatus.Text = result == Error.Ok ? "Saved" : "Could not save. Please try again.";
            if (result != Error.Ok) GD.PrintErr("[svz-settings] audio settings could not be saved: " + result);
            return result;
        }
        public static void SetAudio(double master, double music, double effects, bool muted)
        {
            Master = ClampVolume(master, Master); Music = ClampVolume(music, Music);
            Effects = ClampVolume(effects, Effects); Muted = muted;
            EnsureBuses(); ApplyAudio();
        }
        private static void EnsureBuses()
        {
            if (Ready) return;
            AddBus(MasterBus, "Master"); AddBus(MusicBus, MasterBus); AddBus(EffectsBus, MasterBus);
            Ready = true;
        }
        private static void AddBus(string name, string send)
        {
            if (AudioServer.GetBusIndex(name) >= 0) return;
            int index = AudioServer.BusCount; AudioServer.AddBus();
            AudioServer.SetBusName(index, name); AudioServer.SetBusSend(index, send);
        }
        private static void ApplyAudio()
        {
            SetBus(MasterBus, Master, Muted); SetBus(MusicBus, Music, false); SetBus(EffectsBus, Effects, false);
        }
        private static void SetBus(string name, double value, bool mute)
        {
            int index = AudioServer.GetBusIndex(name);
            AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb((float)Math.Max(0.0001, value / 100)));
            AudioServer.SetBusMute(index, mute || value == 0);
        }
        public static void SetStream(AudioStreamPlayer player, AudioStream stream, string clipName)
        {
            EnsureBuses(); player.Stream = stream;
            bool music = IsMusic(clipName); player.Bus = music ? MusicBus : EffectsBus;
            if (music && !LoggedMusic) { LoggedMusic = true; GD.Print("[svz-settings] music routed to separate bus"); }
            if (!music && !LoggedEffects) { LoggedEffects = true; GD.Print("[svz-settings] effects routed to separate bus"); }
        }
        public static void SetEffectStream(AudioStreamPlayer player, AudioStream stream) => SetStream(player, stream, null);
        public static void RouteVideo(VideoStreamPlayer player) { EnsureBuses(); player.Bus = MasterBus; }

        private static Label Label(string text, int size = 26)
        {
            var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", size);
            return label;
        }
        private static void BuildMenu()
        {
            Layer?.QueueFree();
            Layer = new CanvasLayer { Layer = 110, Name = "AudioSettings", ProcessMode = Node.ProcessModeEnum.Always };
            Root.AddChild(Layer);
            Ui = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = SettingsSkin.Build() }; Layer.AddChild(Ui);
            PauseEntry = new Button { Name = "AudioEntry", Text = "SETTINGS", Visible = false,
                CustomMinimumSize = new Vector2(SettingsSkin.EntryWidth, SettingsSkin.ButtonHeight) };
            PauseEntry.AddThemeFontSizeOverride("font_size", 26); Ui.AddChild(PauseEntry);
            PauseEntry.Pressed += Open;
            Modal = new Control { Visible = false }; Ui.AddChild(Modal); Modal.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var shade = new ColorRect { Color = new Color(0.04f, 0.01f, 0, 0.72f) }; Modal.AddChild(shade);
            shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var center = new CenterContainer(); Modal.AddChild(center); center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            // Parchment scroll as in the game's own dialogs; rollers stay unstretched.
            var panel = new PanelContainer { Name = "AudioPanel", CustomMinimumSize = new Vector2(1000, 690) }; center.AddChild(panel);
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6); panel.AddChild(box);
            var title = Label("SETTINGS", 46); title.HorizontalAlignment = HorizontalAlignment.Center;
            title.AddThemeColorOverride("font_color", SettingsSkin.TitleRed); title.AddThemeConstantOverride("outline_size", 12);
            title.AddThemeColorOverride("font_outline_color", new Color("140302")); box.AddChild(title);
            var subtitle = Label("AUDIO", 24); subtitle.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(subtitle);
            AddSlider(box, "MASTER VOLUME", () => Master, v => SetAudio(v, Music, Effects, Muted));
            AddSlider(box, "MUSIC", () => Music, v => SetAudio(Master, v, Effects, Muted));
            AddSlider(box, "SOUND EFFECTS", () => Effects, v => SetAudio(Master, Music, v, Muted));
            var mute = new CheckButton { Name = "AudioMute", Text = "MUTE ALL AUDIO", ButtonPressed = Muted,
                CustomMinimumSize = new Vector2(0, SettingsSkin.RowHeight + 4) };
            box.AddChild(mute);
            mute.Toggled += enabled => { SetAudio(Master, Music, Effects, enabled); Save(); };
            SaveStatus = Label("Changes save when you close", 19); SaveStatus.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(SaveStatus);
            var done = new Button { Name = "AudioDone", Text = "DONE", Icon = SettingsSkin.CheckIcon, ExpandIcon = false,
                CustomMinimumSize = new Vector2(320, SettingsSkin.ButtonHeight), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
            done.AddThemeFontSizeOverride("font_size", 32); box.AddChild(done); done.Pressed += Close;
            done.Shortcut = new Shortcut { Events = new Godot.Collections.Array { new InputEventKey { Keycode = Key.Escape } } };
        }
        private static void AddSlider(VBoxContainer parent, string name, Func<double> get, Action<double> set)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 18); parent.AddChild(row);
            var label = Label(name, 24); label.CustomMinimumSize = new Vector2(214, SettingsSkin.RowHeight); row.AddChild(label);
            // The whole row height accepts touches; the painted track stays slim like the original HUD bars.
            var slider = new HSlider { Name = name == "MASTER VOLUME" ? "MasterVolume" : name == "MUSIC" ? "MusicVolume" : "EffectsVolume", MinValue = 0, MaxValue = 100, Step = 1, Value = get(),
                CustomMinimumSize = new Vector2(352, SettingsSkin.RowHeight), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddChild(slider);
            var amount = Label($"{get():0}%"); amount.CustomMinimumSize = new Vector2(78, SettingsSkin.RowHeight);
            amount.HorizontalAlignment = HorizontalAlignment.Right; row.AddChild(amount);
            slider.ValueChanged += value => { set(value); amount.Text = $"{value:0}%"; };
            slider.DragEnded += changed => { if (changed) Save(); };
            slider.FocusExited += () => Save();
        }
        private static void Layout()
        {
            var size = Root.GetViewport().GetVisibleRect().Size;
            Scale = Math.Max(0.1, Math.Min(size.X / 1280.0, size.Y / 720.0));
            Layer.Transform = new Transform2D(0, new Vector2((float)Scale, (float)Scale), 0, Vector2.Zero);
            Ui.Size = size / (float)Scale;
            // Options: bottom-left, opposite the original green confirm and clear of the centred credit line.
            // Pause: bottom-right, as before.
            float w = SettingsSkin.EntryWidth, h = SettingsSkin.ButtonHeight;
            PauseEntry.Position = new Vector2(PauseContext ? Ui.Size.X - w - 24 : 24, Ui.Size.Y - h - 16);
            PauseEntry.Size = new Vector2(w, h);
        }
        public static void ShowPauseEntry(bool show) { if (PauseEntry != null) { PauseContext = true; PauseEntry.Text = "SETTINGS"; PauseEntry.Visible = show; Layout(); } }
        public static void ShowOptionsEntry() { if (PauseEntry != null) { EntryClosing = false; PauseContext = false; PauseEntry.Text = "AUDIO SETTINGS"; PauseEntry.Visible = true; Layout(); } }
        public static void SceneChanging() { EntryClosing = true; if (PauseEntry != null) PauseEntry.Visible = false; }
        public static void UpdateOptionsAvailability(bool available)
        {
            if (!IsOpen && !EntryClosing && PauseEntry != null) PauseEntry.Visible = available;
        }
        public static void Open()
        {
            if (Root == null || IsOpen) return;
            EntryWasVisible = PauseEntry.Visible; ClearInput?.Invoke();
            WasPaused = Root.GetTree().Paused;
            if (!BackRestorePending) WasQuitOnBack = Root.GetTree().QuitOnGoBack;
            BackRestorePending = false; BackRestoreToken++;
            Root.GetTree().QuitOnGoBack = false;
            IsOpen = true; Modal.Visible = true; PauseEntry.Visible = false;
            Root.GetTree().Paused = true;
        }
        public static void Close()
        {
            if (!IsOpen) return;
            if (Save() != Error.Ok) return;
            ClearInput?.Invoke(); IgnoreClosingReleases = true; IsOpen = false; Modal.Visible = false;
            Root.GetTree().Paused = WasPaused;
            // Window propagates Back notifications before emitting go_back_requested.
            // Restoring this flag inside that notification would let SceneTree quit.
            int token = ++BackRestoreToken; BackRestorePending = true;
            Callable.From(() => {
                if (token != BackRestoreToken || IsOpen || !GodotObject.IsInstanceValid(Root)) return;
                Root.GetTree().QuitOnGoBack = WasQuitOnBack; BackRestorePending = false;
            }).CallDeferred();
            PauseEntry.Visible = EntryWasVisible;
        }
        public static void OnNotification(int what)
        {
            if (IsOpen && what == (int)Node.NotificationWMGoBackRequest) Close();
        }
        public static bool BlockGameInput(InputEvent input)
        {
            if (IsOpen) return true;
            // A touch can emit both a touch-up and an emulated mouse-up. Ignore
            // closing releases until a fresh press, so Done cannot activate Quit.
            if (IgnoreClosingReleases)
            {
                bool freshPress = input is InputEventScreenTouch { Pressed: true } || input is InputEventMouseButton { Pressed: true } ||
                    input is InputEventKey { Pressed: true };
                if (freshPress) IgnoreClosingReleases = false;
                else if (input is InputEventScreenTouch || input is InputEventScreenDrag || input is InputEventMouseButton || input is InputEventKey) return true;
            }
            if (PauseEntry == null || !PauseEntry.IsVisibleInTree()) return false;
            Vector2? point = input switch { InputEventScreenTouch t => t.Position, InputEventMouseButton m => m.Position,
                InputEventScreenDrag d => d.Position, InputEventMouseMotion m => m.Position, _ => null };
            return point.HasValue && new Rect2(Vector2.Zero, PauseEntry.Size).HasPoint(PauseEntry.GetGlobalTransformWithCanvas().AffineInverse() * point.Value);
        }
    }
}
