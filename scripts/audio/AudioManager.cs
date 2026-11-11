// =========================================================================================
// Original Author : anjivv (Anj Ferrer) - Oct 31, 2026
// Responsibilities: - Global audio playback singleton for music, ambient, and SFX
//                   - Procedural audio synthesis fallback for missing sound assets
//                   - SFX polyphonic player pooling and audio bus management
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | anjivv : Implemented global audio playback singleton, BGM crossfade, ambient loops, and procedural synthesis
// - Oct 31, 2026 | air : Applied standard header formatting and moved to scripts/audio/
// - Nov 06, 2026 | anjivv : Added dynamic GameManager phase tracking, pitched SFX playback, and procedural synthesis for footsteps, door pounds, and flower audio
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;

// Autoload singleton. Project > Project Settings > Autoload > add this script with Node Name "AudioManager".
// Looks for real audio files in AudioFolder (ogg / wav / mp3) named after the sound keys below.
// If a file is missing, a procedurally generated placeholder is used so the game is never silent/crashy.
//
// Sound keys:
//   SFX:     window_rattle, door_thud, door_pound, monster_footsteps, foliage_rustle,
//            jumpscare_stinger, warning_stinger, coin_collect, banner_sting,
//            flower_merge, flower_plant
//   Ambient: ambient_garden, ambient_wind
//   Music:   music_day (generated placeholder tune), music_night (silent if the file is missing)
public partial class AudioManager : Node
{
	public static AudioManager Instance { get; private set; }

	public const string MusicBus = "Music";
	public const string AmbientBus = "Ambient";
	public const string SfxBus = "SFX";

	[ExportGroup("Files")]
	[Export] public string AudioFolder = "res://assets/audio/";

	[ExportGroup("Mixer")]
	[Export] public int SfxPoolSize = 8;
	[Export] public float MusicVolumeDb = -8f;
	[Export] public float DefaultFade = 1.5f;

	[ExportGroup("Daytime Extras (off = daytime is music only)")]
	[Export] public bool EnableDayAmbience = false;
	[Export] public bool EnableDayRustle = false;

	[ExportGroup("Foliage Rustle (seconds between rustles)")]
	[Export] public Vector2 DayRustleInterval = new Vector2(5f, 12f);
	[Export] public Vector2 NightRustleInterval = new Vector2(8f, 18f);

	private const float SilentDb = -60f;

	private readonly Dictionary<string, AudioStream> _cache = new();
	private readonly Dictionary<string, AudioStreamPlayer> _ambientLayers = new();
	private readonly Dictionary<AudioStreamPlayer, Tween> _fades = new();
	private readonly List<AudioStreamPlayer> _sfxPool = new();

	private AudioStreamPlayer _musicA;
	private AudioStreamPlayer _musicB;
	private AudioStreamPlayer _activeMusic;
	private string _currentMusic;

	private bool _rustleEnabled;
	private float _rustleTimer;
	private Vector2 _rustleInterval;
	private float _rustleVolumeDb = -12f;

	public override void _Ready()
	{
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;

		EnsureBus(MusicBus);
		EnsureBus(AmbientBus);
		EnsureBus(SfxBus);

		_musicA = MakePlayer("MusicA", MusicBus);
		_musicB = MakePlayer("MusicB", MusicBus);
		_activeMusic = _musicA;

		for (int i = 0; i < SfxPoolSize; i++)
			_sfxPool.Add(MakePlayer($"Sfx{i}", SfxBus));

		GD.Print("[AudioManager] Ready (autoload is running).");
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}

	public override void _Process(double delta)
	{
		TryHookGameManager((float)delta);
		UpdateRustle((float)delta);
	}

	// Finds the GameManager by itself and follows its day/night phase,
	// so music works even if GameManager.cs never calls AudioManager directly.
	private GameManager _gm;
	private float _hookTimer;

	private void TryHookGameManager(float dt)
	{
		if (IsInstanceValid(_gm))
			return;

		_hookTimer -= dt;
		if (_hookTimer > 0f)
			return;
		_hookTimer = 0.5f;

		_gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		if (_gm == null)
			return;

		_gm.PhaseChanged += OnGamePhaseChanged;
		GD.Print("[AudioManager] Hooked into GameManager.");
		OnPhaseChanged(_gm.CurrentPhase == GameManager.GamePhase.Day);
	}

	private void OnGamePhaseChanged(bool isDay, int currentDay) => OnPhaseChanged(isDay);

	private void UpdateRustle(float delta)
	{
		if (!_rustleEnabled)
			return;

		_rustleTimer -= (float)delta;
		if (_rustleTimer <= 0f)
		{
			PlaySfx("foliage_rustle", _rustleVolumeDb + (float)GD.RandRange(-3.0, 2.0), 0.15f);
			_rustleTimer = RandomRange(_rustleInterval);
		}
	}

	// ---------- Public API ----------

	// Null-safe shortcut: AudioManager.Sfx("door_thud");
	public static void Sfx(string name, float volumeDb = 0f, float pitchVariance = 0f)
	{
		Instance?.PlaySfx(name, volumeDb, pitchVariance);
	}

	// Plays a sound at a fixed pitch (1.0 = normal), e.g. higher pitch for higher flower tiers.
	public static void SfxPitch(string name, float pitchScale, float volumeDb = 0f)
	{
		Instance?.PlaySfxPitched(name, pitchScale, volumeDb);
	}

	public void PlaySfxPitched(string name, float pitchScale, float volumeDb = 0f)
	{
		var stream = LoadStream(name);
		if (stream == null)
			return;

		var player = GetFreeSfxPlayer();
		player.Stream = stream;
		player.VolumeDb = volumeDb;
		player.PitchScale = Mathf.Max(0.1f, pitchScale);
		player.Play();
	}

	public void PlaySfx(string name, float volumeDb = 0f, float pitchVariance = 0f)
	{
		var stream = LoadStream(name);
		if (stream == null)
			return;

		var player = GetFreeSfxPlayer();
		player.Stream = stream;
		player.VolumeDb = volumeDb;
		player.PitchScale = 1f + (pitchVariance > 0f ? ((float)GD.Randf() * 2f - 1f) * pitchVariance : 0f);
		player.Play();
	}

	public void PlayMusic(string name, float fade = -1f)
	{
		if (fade < 0f)
			fade = DefaultFade;

		if (_currentMusic == name)
			return;
		_currentMusic = name;

		var stream = LoadStream(name);
		var old = _activeMusic;
		var next = old == _musicA ? _musicB : _musicA;
		_activeMusic = next;

		if (old.Playing)
			FadeTo(old, SilentDb, fade, true);

		if (stream == null)
			return;

		MakeLooping(stream);
		next.Stream = stream;
		GD.Print($"[AudioManager] Playing music '{name}' ({stream.GetType().Name}, {stream.GetLength():0.0}s, {MusicVolumeDb} dB)");
		next.VolumeDb = SilentDb;
		next.Play();
		FadeTo(next, MusicVolumeDb, fade);
	}

	public void StopMusic(float fade = -1f)
	{
		if (fade < 0f)
			fade = DefaultFade;

		_currentMusic = null;
		if (_activeMusic.Playing)
			FadeTo(_activeMusic, SilentDb, fade, true);
	}

	public void PlayAmbient(string name, float volumeDb = -10f, float fade = 2f)
	{
		var stream = LoadStream(name);
		if (stream == null)
			return;

		if (!_ambientLayers.TryGetValue(name, out var player))
		{
			player = MakePlayer($"Ambient_{name}", AmbientBus);
			_ambientLayers[name] = player;
		}

		if (!player.Playing)
		{
			MakeLooping(stream);
			player.Stream = stream;
			player.VolumeDb = SilentDb;
			player.Play();
		}

		FadeTo(player, volumeDb, fade);
	}

	public void StopAmbient(string name, float fade = 2f)
	{
		if (_ambientLayers.TryGetValue(name, out var player) && player.Playing)
			FadeTo(player, SilentDb, fade, true);
	}

	// Silences music, ambience and foliage rustles (SFX keep playing so stingers still work).
	public void StopAll(float fade = 0.5f)
	{
		_rustleEnabled = false;
		StopMusic(fade);
		foreach (var key in _ambientLayers.Keys)
			StopAmbient(key, fade);
	}

	// Called by GameManager at the start of each phase.
	public void OnPhaseChanged(bool isDay)
	{
		GD.Print($"[AudioManager] OnPhaseChanged(isDay: {isDay})");
		_rustleEnabled = isDay ? EnableDayRustle : true;
		_rustleInterval = isDay ? DayRustleInterval : NightRustleInterval;
		_rustleVolumeDb = isDay ? -14f : -8f;
		_rustleTimer = RandomRange(_rustleInterval);

		if (isDay)
		{
			if (EnableDayAmbience)
				PlayAmbient("ambient_garden", -10f);
			else
				StopAmbient("ambient_garden");

			StopAmbient("ambient_wind");
			PlayMusic("music_day");
		}
		else
		{
			PlayAmbient("ambient_wind", -8f);
			PlayAmbient("ambient_garden", -22f); // faint night crickets
			PlayMusic("music_night");
		}
	}

	// Called by GameManager when the night is survived (before the celebration).
	public void OnNightSurvived()
	{
		_rustleEnabled = false;
		StopAmbient("ambient_wind", 1.5f);
	}

	public void SetBusVolume(string bus, float linear)
	{
		int idx = AudioServer.GetBusIndex(bus);
		if (idx >= 0)
			AudioServer.SetBusVolumeDb(idx, Mathf.LinearToDb(Mathf.Max(linear, 0.0001f)));
	}

	// ---------- Internals ----------

	private static void EnsureBus(string name)
	{
		if (AudioServer.GetBusIndex(name) != -1)
			return;

		AudioServer.AddBus();
		int idx = AudioServer.BusCount - 1;
		AudioServer.SetBusName(idx, name);
		AudioServer.SetBusSend(idx, "Master");
	}

	private AudioStreamPlayer MakePlayer(string name, string bus)
	{
		var p = new AudioStreamPlayer { Name = name, Bus = bus };
		AddChild(p);
		return p;
	}

	private AudioStreamPlayer GetFreeSfxPlayer()
	{
		foreach (var p in _sfxPool)
		{
			if (!p.Playing)
				return p;
		}
		return _sfxPool[0]; // all busy: steal the first
	}

	private void FadeTo(AudioStreamPlayer p, float db, float fade, bool stopWhenDone = false)
	{
		if (_fades.TryGetValue(p, out var old) && old.IsValid())
			old.Kill();

		var tw = CreateTween();
		tw.TweenProperty(p, "volume_db", db, Mathf.Max(0.01f, fade));
		if (stopWhenDone)
			tw.TweenCallback(Callable.From(p.Stop));
		_fades[p] = tw;
	}

	private static float RandomRange(Vector2 r)
	{
		return (float)GD.RandRange(r.X, r.Y);
	}

	private static void MakeLooping(AudioStream s)
	{
		switch (s)
		{
			case AudioStreamOggVorbis ogg: ogg.Loop = true; break;
			case AudioStreamMP3 mp3: mp3.Loop = true; break;
		}
	}

	private AudioStream LoadStream(string name)
	{
		if (string.IsNullOrEmpty(name))
			return null;

		if (_cache.TryGetValue(name, out var cached))
			return cached;

		AudioStream stream = null;
		foreach (var ext in new[] { "ogg", "wav", "mp3" })
		{
			string path = $"{AudioFolder}{name}.{ext}";
			if (ResourceLoader.Exists(path))
			{
				var loaded = GD.Load<AudioStream>(path);
				if (loaded != null)
				{
					stream = loaded;
					GD.Print($"[AudioManager] Loaded '{name}' from {path}");
					break;
				}

				// File is listed but could not be read (stale/broken import): try the next format.
				GD.PushWarning($"[AudioManager] {path} exists but failed to load. Trying the next format...");
			}
		}

		if (stream == null)
		{
			stream = Generate(name);
			GD.Print($"[AudioManager] No file for '{name}' in {AudioFolder}, using {(stream != null ? "generated placeholder" : "silence")}.");
		}

		_cache[name] = stream;
		return stream;
	}

	// ---------- Procedural placeholders ----------

	private static float Noise() => (float)GD.Randf() * 2f - 1f;

	// One heavy footstep starting at t0: low thump + a short crunch of noise.
	private static float Step(float t, float t0)
	{
		float d = t - t0;
		if (d < 0f || d > 0.5f)
			return 0f;

		float thump = Mathf.Sin(d * Mathf.Tau * (60f - 30f * d)) * Mathf.Exp(-d * 9f);
		float crunch = Noise() * Mathf.Exp(-d * 40f) * 0.35f;
		return thump * 0.9f + crunch;
	}

	// One heavy door knock starting at t0.
	private static float Knock(float t, float t0)
	{
		float d = t - t0;
		if (d < 0f || d > 0.4f)
			return 0f;

		float boom = Mathf.Sin(d * Mathf.Tau * (90f - 40f * d)) * Mathf.Exp(-d * 12f);
		float wood = Noise() * Mathf.Exp(-d * 45f) * 0.5f;
		return boom * 0.9f + wood;
	}

	private static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

	// Sine tone with the phase wrapped in double precision so long songs stay clean.
	private static float Tone(float t, float freq) =>
		Mathf.Sin((float)(((double)t * freq % 1.0) * Mathf.Tau));

	private static AudioStreamWav MakeWav(float duration, Func<float, float> gen, bool loop = false, int rate = 22050)
	{
		int n = (int)(rate * duration);
		var data = new byte[n * 2];

		for (int i = 0; i < n; i++)
		{
			float v = Mathf.Clamp(gen((float)i / rate), -1f, 1f);
			short s = (short)(v * 32767f);
			data[i * 2] = (byte)(s & 0xFF);
			data[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
		}

		var wav = new AudioStreamWav
		{
			Format = AudioStreamWav.FormatEnum.Format16Bits,
			MixRate = rate,
			Data = data
		};

		if (loop)
		{
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopBegin = 0;
			wav.LoopEnd = n;
		}

		return wav;
	}

	private static AudioStream Generate(string name)
	{
		float lp = 0f; // low-pass state shared by the noise-based sounds

		switch (name)
		{
			case "window_rattle":
				return MakeWav(0.7f, t =>
				{
					float env = Mathf.Exp(-t * 3f);
					float gate = Mathf.Sin(t * Mathf.Tau * 22f) > 0f ? 1f : 0.25f;
					return (Noise() * 0.5f + Mathf.Sin(t * Mathf.Tau * 1800f) * 0.15f) * gate * env;
				});

			case "door_thud":
				return MakeWav(0.6f, t =>
					Mathf.Sin(t * Mathf.Tau * (55f - 20f * t)) * Mathf.Exp(-t * 7f) * 0.9f
					+ Noise() * Mathf.Exp(-t * 30f) * 0.4f);

			case "foliage_rustle":
				return MakeWav(1.6f, t =>
				{
					float n = Noise();
					lp += 0.2f * (n - lp);
					float hp = n - lp;
					float swell = Mathf.Pow(Mathf.Sin(Mathf.Pi * t / 1.6f), 2f);
					return hp * swell * (0.6f + 0.4f * Mathf.Sin(t * Mathf.Tau * 7f)) * 0.5f;
				});

			case "jumpscare_stinger":
				return MakeWav(1.4f, t =>
				{
					float w1 = Mathf.Sign(Mathf.Sin(t * Mathf.Tau * 850f));
					float w2 = Mathf.Sign(Mathf.Sin(t * Mathf.Tau * 1150f));
					float env = Mathf.Clamp(1f - t / 1.4f, 0f, 1f);
					return (w1 * 0.4f + w2 * 0.4f + Noise() * 0.4f) * env * 0.9f;
				});

			case "warning_stinger":
				return MakeWav(0.8f, t =>
				{
					float env = 1f - t / 0.8f;
					return (Mathf.Sin(t * Mathf.Tau * 110f) * 0.6f + Mathf.Sin(t * Mathf.Tau * 440f) * 0.3f) * env;
				});

			case "monster_footsteps":
				return MakeWav(1.3f, t => (Step(t, 0f) + Step(t, 0.65f)) * 0.9f);

			case "door_pound":
				return MakeWav(1.2f, t => (Knock(t, 0f) + Knock(t, 0.35f) + Knock(t, 0.7f)) * 0.9f);

			case "flower_merge":
			{
				// Rising sparkle chime with a soft pop at the start
				float[] notes = { 1046.5f, 1318.5f, 1568f, 2093f };
				return MakeWav(0.9f, t =>
				{
					float pop = Mathf.Sin(t * Mathf.Tau * (300f + 900f * t)) * Mathf.Exp(-t * 30f) * 0.4f;
					float sum = 0f;
					for (int i = 0; i < notes.Length; i++)
					{
						float d = t - 0.07f * i - 0.04f;
						if (d < 0f)
							continue;
						sum += (Tone(t, notes[i]) + 0.25f * Tone(t, notes[i] * 2f)) * Mathf.Exp(-d * 6f) * 0.18f;
					}
					return pop + sum;
				});
			}

			case "flower_plant":
				return MakeWav(0.25f, t =>
					Mathf.Sin(t * Mathf.Tau * (180f - 100f * t)) * Mathf.Exp(-t * 18f) * 0.7f
					+ Noise() * Mathf.Exp(-t * 60f) * 0.2f);

			case "coin_collect":
				return MakeWav(0.25f, t =>
					Mathf.Sin(t * Mathf.Tau * (t < 0.07f ? 988f : 1319f)) * Mathf.Exp(-t * 10f) * 0.5f);

			case "banner_sting":
				return MakeWav(1.4f, t =>
				{
					float env = Mathf.Min(t / 0.05f, 1f) * Mathf.Exp(-t * 2.2f);
					float chord = Mathf.Sin(t * Mathf.Tau * 261.6f) + Mathf.Sin(t * Mathf.Tau * 329.6f) + Mathf.Sin(t * Mathf.Tau * 392f);
					return chord * 0.2f * env;
				});

			case "ambient_wind":
				return MakeWav(4f, t =>
				{
					lp += 0.02f * (Noise() - lp);
					float mod = 0.6f + 0.4f * Mathf.Sin(t * Mathf.Tau * 0.25f);
					return lp * mod * 3f;
				}, true);

			case "ambient_garden":
				return MakeWav(4f, t =>
				{
					lp += 0.05f * (Noise() - lp);
					float chirpGate = Mathf.Sin(t * Mathf.Tau * 5f) > 0.3f ? 1f : 0f;
					float chirp = Mathf.Sin(t * Mathf.Tau * 4200f) * 0.06f * chirpGate;
					return lp * 0.8f + chirp;
				}, true);

			case "music_day":
			{
				// Calm looping farm tune: C - Am - F - G, 88 BPM, 8 bars (~21s)
				int[][] chords = { new[] { 60, 64, 67 }, new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 55, 59, 62 } };
				int[] bassNotes = { 48, 45, 41, 43 };
				int[] patA = { 0, 1, 2, 1, 0, 1, 2, -1 };
				int[] patB = { 2, 1, 0, 1, 2, 1, 0, -1 };
				float beat = 60f / 88f;
				float bar = beat * 4f;
				float eighth = beat * 0.5f;

				return MakeWav(bar * 8f, t =>
				{
					int barIdx = Mathf.Min((int)(t / bar), 7);
					int c = barIdx % 4;
					float tb = t - barIdx * bar;
					int[] chord = chords[c];

					// Soft pad
					float padEnv = Mathf.Min(tb / 0.3f, 1f) * Mathf.Min((bar - tb) / 0.3f, 1f);
					float pad = 0f;
					foreach (int m in chord)
						pad += Tone(t, Midi(m));
					pad *= 0.055f * padEnv;

					// Bass on beats 1 and 3
					float tBass = tb % (beat * 2f);
					float bassEnv = Mathf.Exp(-tBass * 3.5f) * Mathf.Min(tBass / 0.01f, 1f);
					float bassOut = Tone(t, Midi(bassNotes[c])) * 0.2f * bassEnv;

					// Music-box arpeggio
					int e = Mathf.Min((int)(tb / eighth), 7);
					int idx = (barIdx < 4 ? patA : patB)[e];
					float melody = 0f;
					if (idx >= 0)
					{
						float te = tb - e * eighth;
						float f = Midi(chord[idx] + 12);
						float env = Mathf.Exp(-te * 6f) * Mathf.Min(te / 0.005f, 1f);
						melody = (Tone(t, f) + 0.3f * Tone(t, f * 2f)) * 0.12f * env;
					}

					return pad + bassOut + melody;
				}, true);
			}

			default:
				return null; // music and unknown keys have no placeholder
		}
	}
}
