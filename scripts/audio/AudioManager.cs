// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Central audio subsystem and audio bus manager (Master, Music, SFX)
//                   - Decibel attenuation and linear volume slider binding
//                   - Sound playback API (music streams and spatial/non-spatial SFX)
//                   - Persistent audio preference synchronization with SaveManager
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented AudioManager with dynamic bus creation and volume persistence
// =========================================================================================
using Godot;
using System;

public partial class AudioManager : Node
{
	public static AudioManager Instance { get; private set; }

	public const string BusMaster = "Master";
	public const string BusMusic = "Music";
	public const string BusSFX = "SFX";

	private AudioStreamPlayer _musicPlayer;

	public override void _EnterTree()
	{
		if (Instance != null && Instance != this)
		{
			QueueFree();
			return;
		}
		Instance = this;

		InitializeAudioBuses();
		CreateMusicPlayer();
		ApplySavedSettings();
	}

	private void InitializeAudioBuses()
	{
		EnsureBus(BusMusic, BusMaster);
		EnsureBus(BusSFX, BusMaster);
	}

	private void EnsureBus(string busName, string sendTo)
	{
		int idx = AudioServer.GetBusIndex(busName);
		if (idx == -1)
		{
			AudioServer.AddBus();
			idx = AudioServer.BusCount - 1;
			AudioServer.SetBusName(idx, busName);
			AudioServer.SetBusSend(idx, sendTo);
		}
	}

	private void CreateMusicPlayer()
	{
		_musicPlayer = new AudioStreamPlayer
		{
			Name = "MusicPlayer",
			Bus = BusMusic
		};
		AddChild(_musicPlayer);
	}

	public void ApplySavedSettings()
	{
		var settings = SaveManager.CurrentData.Audio;
		SetMasterVolume(settings.MasterVolume, false);
		SetMusicVolume(settings.MusicVolume, false);
		SetSFXVolume(settings.SFXVolume, false);

		if (settings.Fullscreen)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		}
	}

	public void SetMasterVolume(float linear, bool save = true)
	{
		SetBusVolumeLinear(BusMaster, linear);
		if (save)
		{
			SaveManager.CurrentData.Audio.MasterVolume = linear;
			SaveManager.SaveDirect();
		}
	}

	public void SetMusicVolume(float linear, bool save = true)
	{
		SetBusVolumeLinear(BusMusic, linear);
		if (save)
		{
			SaveManager.CurrentData.Audio.MusicVolume = linear;
			SaveManager.SaveDirect();
		}
	}

	public void SetSFXVolume(float linear, bool save = true)
	{
		SetBusVolumeLinear(BusSFX, linear);
		if (save)
		{
			SaveManager.CurrentData.Audio.SFXVolume = linear;
			SaveManager.SaveDirect();
		}
	}

	public float GetMasterVolume() => GetBusVolumeLinear(BusMaster);
	public float GetMusicVolume() => GetBusVolumeLinear(BusMusic);
	public float GetSFXVolume() => GetBusVolumeLinear(BusSFX);

	private void SetBusVolumeLinear(string busName, float linear)
	{
		int idx = AudioServer.GetBusIndex(busName);
		if (idx == -1) return;

		linear = Mathf.Clamp(linear, 0.0f, 1.0f);
		if (linear <= 0.001f)
		{
			AudioServer.SetBusMute(idx, true);
		}
		else
		{
			AudioServer.SetBusMute(idx, false);
			float db = Mathf.LinearToDb(linear);
			AudioServer.SetBusVolumeDb(idx, db);
		}
	}

	private float GetBusVolumeLinear(string busName)
	{
		int idx = AudioServer.GetBusIndex(busName);
		if (idx == -1) return 1.0f;

		if (AudioServer.IsBusMute(idx))
			return 0.0f;

		float db = AudioServer.GetBusVolumeDb(idx);
		return Mathf.DbToLinear(db);
	}

	public void PlaySFX(AudioStream stream, float pitch = 1.0f)
	{
		if (stream == null) return;

		var player = new AudioStreamPlayer
		{
			Stream = stream,
			Bus = BusSFX,
			PitchScale = pitch
		};
		AddChild(player);
		player.Finished += player.QueueFree;
		player.Play();
	}

	public void PlayMusic(AudioStream stream)
	{
		if (_musicPlayer == null || stream == null) return;
		if (_musicPlayer.Stream == stream && _musicPlayer.Playing) return;

		_musicPlayer.Stream = stream;
		_musicPlayer.Play();
	}

	public void StopMusic()
	{
		_musicPlayer?.Stop();
	}
}
