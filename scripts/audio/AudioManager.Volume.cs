// =========================================================================================
// Original Author : air (Airless-KV) - Oct 31, 2026
// Responsibilities: - Volume control and bus attenuation for Master, Music, and SFX
//                   - Linear-to-decibel conversion and SaveManager persistence
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Added partial volume methods to interface with UI menus and SaveManager
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
