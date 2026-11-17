using System;
using System.Collections.Generic;
using Svz;
using G = Godot;

namespace UnityEngine
{
	public sealed partial class AudioClip
	{
		internal string __resPath;
		internal G.AudioStream __stream;
		internal float __length = -1f;
		internal int __channels = 1, __frequency = 22050;

		internal G.AudioStream __Stream
		{
			get
			{
				if (__stream == null && __resPath != null)
				{
					__stream = G.ResourceLoader.Load<G.AudioStream>(__resPath);
					if (__stream == null) G.GD.PrintErr("[svz] missing audio " + __resPath);
				}
				return __stream;
			}
		}

		public partial float length => __length >= 0 ? __length : (float)(__Stream?.GetLength() ?? 0.0);
		public partial int samples => (int)(length * __frequency);
		public partial int channels => __channels;
		public partial int frequency => __frequency;
		public partial bool isReadyToPlay => true;
	}

	public sealed partial class AudioSource
	{
		internal AudioClip __clip;
		internal float __volume = 1f, __pitch = 1f, __minDist = 1f, __maxDist = 500f, __pan, __pan3d = 1f;
		internal bool __loop, __playOnAwake = true, __mute, __ignoreListener;
		internal int __priority = 128;
		internal AudioRolloffMode __rolloff;
		internal G.AudioStreamPlayer __player;
		private bool __paused;

		private G.AudioStreamPlayer Player
		{
			get
			{
				if (__player == null)
				{
					__player = new G.AudioStreamPlayer { Name = "Audio_" + (gameObject?.name ?? "") };
					Main.Instance.AddChild(__player);
				}
				return __player;
			}
		}

		internal float __EffectiveVolume => __mute ? 0f : __volume;

		private void Apply()
		{
			if (__player == null) return;
			__player.VolumeDb = G.Mathf.LinearToDb(Mathf.Max(0.0001f, __EffectiveVolume));
			__player.PitchScale = Mathf.Clamp(Mathf.Abs(__pitch), 0.01f, 4f);
		}

		public partial float volume { get => __volume; set { __volume = Mathf.Clamp01(value); Apply(); } }
		public partial float pitch { get => __pitch; set { __pitch = value; Apply(); } }

		public partial float time
		{
			get => __player != null && __player.Playing ? __player.GetPlaybackPosition() : 0f;
			set { if (__player != null && __player.Playing) __player.Seek(value); }
		}

		public partial int timeSamples { get => (int)(time * (__clip?.__frequency ?? 22050)); set => time = value / (float)(__clip?.__frequency ?? 22050); }
		public partial AudioClip clip { get => __clip; set => __clip = value; }
		public partial bool isPlaying => __player != null && __player.Playing && !__paused;
		public partial bool loop { get => __loop; set => __loop = value; }
		public partial bool ignoreListenerVolume { get => __ignoreListener; set => __ignoreListener = value; }
		public partial bool playOnAwake { get => __playOnAwake; set => __playOnAwake = value; }
		public partial AudioVelocityUpdateMode velocityUpdateMode { get => AudioVelocityUpdateMode.Auto; set { } }
		public partial float panLevel { get => __pan3d; set => __pan3d = value; }
		public partial bool bypassEffects { get => false; set { } }
		public partial float dopplerLevel { get => 0f; set { } }
		public partial float spread { get => 0f; set { } }
		public partial int priority { get => __priority; set => __priority = value; }
		public partial bool mute { get => __mute; set { __mute = value; Apply(); } }
		public partial float minDistance { get => __minDist; set => __minDist = value; }
		public partial float maxDistance { get => __maxDist; set => __maxDist = value; }
		public partial float pan { get => __pan; set => __pan = value; }
		public partial AudioRolloffMode rolloffMode { get => __rolloff; set => __rolloff = value; }
		public partial float minVolume { get => 0f; set { } }
		public partial float maxVolume { get => 1f; set { } }
		public partial float rolloffFactor { get => 1f; set { } }

		public partial void Play(ulong delay)
		{
			var s = __clip?.__Stream;
			if (s == null) return;
			var p = Player;
			p.Stream = AudioSystem.Looping(s, __loop);
			Apply();
			__paused = false;
			p.StreamPaused = false;
			p.Play();
		}

		private static partial void INTERNAL_CALL_Stop(AudioSource self)
		{
			self.__paused = false;
			self.__player?.Stop();
		}

		private static partial void INTERNAL_CALL_Pause(AudioSource self)
		{
			if (self.__player == null) return;
			self.__paused = true;
			self.__player.StreamPaused = true;
		}

		public partial void PlayOneShot(AudioClip clip, float volumeScale)
		{
			var s = clip?.__Stream;
			if (s == null) return;
			AudioSystem.OneShot(AudioSystem.Looping(s, false), __EffectiveVolume * volumeScale, __pitch);
		}

		private partial void GetOutputDataHelper(float[] samples, int channel) => Array.Clear(samples, 0, samples.Length);
		private partial void GetSpectrumDataHelper(float[] samples, int channel, FFTWindow window) => Array.Clear(samples, 0, samples.Length);

		internal override void __OnLoaded()
		{
			if (__playOnAwake && !__go.__isAsset && __go.__active && __enabled) Play(0);
		}

		internal override void __OnActiveChanged()
		{
			if (__go.__active) { if (__playOnAwake && __enabled && !isPlaying) Play(0); }
			else __player?.Stop();
		}

		internal override void __OnEnabledChanged()
		{
			if (!__enabled) __player?.Stop();
			else if (__playOnAwake && __go != null && __go.__active) Play(0);
		}

		internal override void __OnDestroyNative()
		{
			__player?.QueueFree();
			__player = null;
		}

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (AudioSource)src;
			__clip = s.__clip; __volume = s.__volume; __pitch = s.__pitch; __loop = s.__loop; __playOnAwake = s.__playOnAwake;
			__mute = s.__mute; __minDist = s.__minDist; __maxDist = s.__maxDist; __rolloff = s.__rolloff; __priority = s.__priority;
		}
	}

	public sealed partial class AudioListener
	{
		private static float sVolume = 1f;
		private static bool sPause;

		public static partial float volume
		{
			get => sVolume;
			set { sVolume = Mathf.Clamp01(value); G.AudioServer.SetBusVolumeDb(0, G.Mathf.LinearToDb(Mathf.Max(0.0001f, sVolume))); }
		}

		public static partial bool pause
		{
			get => sPause;
			set { sPause = value; G.AudioServer.SetBusMute(0, value); }
		}

		public partial AudioVelocityUpdateMode velocityUpdateMode { get => AudioVelocityUpdateMode.Auto; set { } }
		private static partial void GetOutputDataHelper(float[] samples, int channel) => Array.Clear(samples, 0, samples.Length);
		private static partial void GetSpectrumDataHelper(float[] samples, int channel, FFTWindow window) => Array.Clear(samples, 0, samples.Length);
	}

	public sealed partial class AudioSettings
	{
		public static partial AudioSpeakerMode driverCaps => AudioSpeakerMode.Stereo;
		public static partial AudioSpeakerMode speakerMode { get => AudioSpeakerMode.Stereo; set { } }
		public static partial int outputSampleRate => (int)G.AudioServer.GetMixRate();
	}
}

namespace Svz
{
	public static class AudioSystem
	{
		private static readonly List<G.AudioStreamPlayer> sPool = new List<G.AudioStreamPlayer>();
		private static readonly Dictionary<(G.AudioStream, bool), G.AudioStream> sLoopVariants = new Dictionary<(G.AudioStream, bool), G.AudioStream>();

		/// Godot keeps looping on the stream resource; make per-loop-mode copies
		public static G.AudioStream Looping(G.AudioStream s, bool loop)
		{
			if (sLoopVariants.TryGetValue((s, loop), out var v)) return v;
			switch (s)
			{
				case G.AudioStreamWav w:
					var wc = (G.AudioStreamWav)w.Duplicate();
					wc.LoopMode = loop ? G.AudioStreamWav.LoopModeEnum.Forward : G.AudioStreamWav.LoopModeEnum.Disabled;
					if (loop) { wc.LoopBegin = 0; wc.LoopEnd = (int)(w.GetLength() * w.MixRate); }
					v = wc; break;
				case G.AudioStreamMP3 m:
					var mc = (G.AudioStreamMP3)m.Duplicate(); mc.Loop = loop; v = mc; break;
				case G.AudioStreamOggVorbis o:
					var oc = (G.AudioStreamOggVorbis)o.Duplicate(); oc.Loop = loop; v = oc; break;
				default: v = s; break;
			}
			sLoopVariants[(s, loop)] = v;
			return v;
		}

		public static void OneShot(G.AudioStream s, float volume, float pitch)
		{
			G.AudioStreamPlayer p = null;
			foreach (var x in sPool) if (!x.Playing) { p = x; break; }
			if (p == null)
			{
				if (sPool.Count >= 32) p = sPool[0];
				else { p = new G.AudioStreamPlayer(); Main.Instance.AddChild(p); sPool.Add(p); }
			}
			p.Stream = s;
			p.VolumeDb = G.Mathf.LinearToDb(Math.Max(0.0001f, volume));
			p.PitchScale = Math.Clamp(Math.Abs(pitch), 0.01f, 4f);
			p.Play();
		}
	}

	public static partial class Builtins
	{
		static partial void RegisterAudio()
		{
			AssetDb.Register("AudioClip", (o, d, ld) =>
			{
				var c = (UnityEngine.AudioClip)o;
				string f = d.Str("$file");
				if (!string.IsNullOrEmpty(f)) c.__resPath = "res://" + f;
				c.__channels = 1;
			});
			AssetDb.Register("AudioSource", (o, d, ld) =>
			{
				var s = (UnityEngine.AudioSource)o;
				s.__clip = ld.Ref<UnityEngine.AudioClip>(d.Get("m_audioClip"));
				s.__playOnAwake = d.Bool("m_PlayOnAwake", true);
				s.__volume = d.Float("m_Volume", 1f);
				s.__pitch = d.Float("m_Pitch", 1f);
				s.__loop = d.Bool("Loop");
				s.__mute = d.Bool("Mute");
				s.__priority = d.Int("Priority", 128);
				s.__minDist = d.Float("MinDistance", 1f);
				s.__maxDist = d.Float("MaxDistance", 500f);
				s.__pan = d.Float("Pan2D");
				s.__rolloff = (UnityEngine.AudioRolloffMode)d.Int("rolloffMode");
			});
		}
	}
}
