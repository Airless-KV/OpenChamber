// =========================================================================================
// Original Author : air (Airless-KV) - Oct 31, 2026
// Responsibilities: - Volume control and bus attenuation for Master, Music, and SFX
//                   - Linear-to-decibel conversion and SaveManager persistence
//                   - Shared UI slider binder helper for MainMenu and PauseMenu
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Added partial volume methods to interface with UI menus and SaveManager
// - Oct 31, 2026 | air : Added AudioSettingsBinder to eliminate duplicate slider wiring across UI menus
// =========================================================================================
using Godot;

public partial class AudioManager
{
	public const string BusMaster = "Master";

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
		SetBusVolumeLinear(MusicBus, linear);
		if (save)
		{
			SaveManager.CurrentData.Audio.MusicVolume = linear;
			SaveManager.SaveDirect();
		}
	}

	public void SetSFXVolume(float linear, bool save = true)
	{
		SetBusVolumeLinear(SfxBus, linear);
		if (save)
		{
			SaveManager.CurrentData.Audio.SFXVolume = linear;
			SaveManager.SaveDirect();
		}
	}

	public float GetMasterVolume() => GetBusVolumeLinear(BusMaster);
	public float GetMusicVolume() => GetBusVolumeLinear(MusicBus);
	public float GetSFXVolume() => GetBusVolumeLinear(SfxBus);

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
}

public static class AudioSettingsBinder
{
	public static void BindSliders(
		HSlider masterSlider, Label masterLabel,
		HSlider musicSlider, Label musicLabel,
		HSlider sfxSlider, Label sfxLabel)
	{
		SyncSliders(masterSlider, masterLabel, musicSlider, musicLabel, sfxSlider, sfxLabel);

		if (masterSlider != null)
		{
			masterSlider.ValueChanged += (val) =>
			{
				AudioManager.Instance?.SetMasterVolume((float)val / 100.0f);
				if (masterLabel != null) masterLabel.Text = $"{Mathf.RoundToInt(val)}%";
			};
		}

		if (musicSlider != null)
		{
			musicSlider.ValueChanged += (val) =>
			{
				AudioManager.Instance?.SetMusicVolume((float)val / 100.0f);
				if (musicLabel != null) musicLabel.Text = $"{Mathf.RoundToInt(val)}%";
			};
		}

		if (sfxSlider != null)
		{
			sfxSlider.ValueChanged += (val) =>
			{
				AudioManager.Instance?.SetSFXVolume((float)val / 100.0f);
				if (sfxLabel != null) sfxLabel.Text = $"{Mathf.RoundToInt(val)}%";
			};
		}
	}

	public static void SyncSliders(
		HSlider masterSlider, Label masterLabel,
		HSlider musicSlider, Label musicLabel,
		HSlider sfxSlider, Label sfxLabel)
	{
		var audio = AudioManager.Instance;
		float master = audio != null ? audio.GetMasterVolume() : SaveManager.CurrentData.Audio.MasterVolume;
		float music = audio != null ? audio.GetMusicVolume() : SaveManager.CurrentData.Audio.MusicVolume;
		float sfx = audio != null ? audio.GetSFXVolume() : SaveManager.CurrentData.Audio.SFXVolume;

		if (masterSlider != null) masterSlider.Value = master * 100.0f;
		if (musicSlider != null) musicSlider.Value = music * 100.0f;
		if (sfxSlider != null) sfxSlider.Value = sfx * 100.0f;

		if (masterLabel != null) masterLabel.Text = $"{Mathf.RoundToInt(master * 100.0f)}%";
		if (musicLabel != null) musicLabel.Text = $"{Mathf.RoundToInt(music * 100.0f)}%";
		if (sfxLabel != null) sfxLabel.Text = $"{Mathf.RoundToInt(sfx * 100.0f)}%";
	}
}
