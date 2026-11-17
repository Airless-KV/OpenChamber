// =========================================================================================
// Original Author : air (Airless-KV) - Oct 31, 2026
// Responsibilities: - Volume control and bus attenuation for Master, Music, and SFX
//                   - Linear-to-decibel conversion and SaveManager persistence
//                   - Shared UI slider binder helper for MainMenu and PauseMenu
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 31, 2026 | air : Added partial volume methods to interface with UI menus and SaveManager
// - Oct 31, 2026 | air : Added AudioSettingsBinder to eliminate duplicate slider wiring across UI menus
// - Nov 15, 2026 | air : Expanded SettingsBinder with resolution, aspect scaling, VSync, FOV, sensitivity, and accessibility
// - Nov 15, 2026 | air : Fixed resolution window centering, unmaximizing, fullscreen synchronization, and aspect scaling canvas items mode
// - Nov 15, 2026 | air : Added RenderScale, ScalingMode, MaxFps, and ShowFpsCounter support in SettingsBinder
// - Nov 15, 2026 | air : Fixed resolution coupling to 3D Viewport scaling and locked UI base canvas to 1080p
// - Nov 15, 2026 | air : Expanded 3D render scale range down to 25% for ultra low-spec optimization
// - Nov 15, 2026 | air : Added _isSyncingUI guard in SettingsBinder to prevent control binding feedback loop
// - Nov 15, 2026 | air : Added Rendering Backend selector, Lite Mode preset, GPU info detector, and restart trigger in SettingsBinder
// - Nov 15, 2026 | air : Prevented FSR1 mode warning under OpenGL Compatibility renderer by defaulting to Bilinear
// - Nov 15, 2026 | air : Fixed asynchronous fullscreen race condition and prevented WindowSetSize during Fullscreen mode
// - Nov 17, 2026 | air : Added ConfirmExitActions checkbox binding and persistence in SettingsBinder
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

public static class SettingsBinder
{
	private static bool _isSyncingUI = false;

	public static readonly (int Width, int Height, string Label)[] SupportedResolutions =
	{
		(1920, 1080, "1920 × 1080 (16:9 FHD)"),
		(2560, 1440, "2560 × 1440 (16:9 QHD)"),
		(1600, 900,  "1600 × 900 (16:9 HD+)"),
		(1366, 768,  "1366 × 768 (16:9 HD)"),
		(1280, 720,  "1280 × 720 (16:9 HD)"),
		(1920, 1200, "1920 × 1200 (16:10 WUXGA)"),
		(1280, 800,  "1280 × 800 (16:10 WXGA)"),
		(1024, 768,  "1024 × 768 (4:3 XGA)")
	};

	public static readonly (int Mode, string Label)[] AspectModes =
	{
		(0, "Expand (Fill Screen)"),
		(1, "Keep (Letterbox / Pillarbox)"),
		(2, "Keep Width"),
		(3, "Keep Height")
	};

	public static void PopulateResolutions(OptionButton optionBtn)
	{
		if (optionBtn == null) return;
		optionBtn.Clear();
		for (int i = 0; i < SupportedResolutions.Length; i++)
		{
			optionBtn.AddItem(SupportedResolutions[i].Label, i);
		}
	}

	public static void PopulateAspectModes(OptionButton optionBtn)
	{
		if (optionBtn == null) return;
		optionBtn.Clear();
		for (int i = 0; i < AspectModes.Length; i++)
		{
			optionBtn.AddItem(AspectModes[i].Label, i);
		}
	}

	public static readonly (int Fps, string Label)[] MaxFpsOptions =
	{
		(60,  "60 FPS (Standard / Stable)"),
		(30,  "30 FPS (Power Saver / HD 620)"),
		(0,   "Unlimited (Uncapped)"),
		(90,  "90 FPS"),
		(120, "120 FPS"),
		(144, "144 FPS"),
		(240, "240 FPS")
	};

	public static readonly (int Mode, string Label)[] ScalingModeOptions =
	{
		(0, "Bilinear (Zero Overhead - Recommended)"),
		(1, "AMD FSR 1.0 (Edge Contrast Sharpening)")
	};

	public static readonly (string Method, string Label)[] RenderingMethodOptions =
	{
		("gl_compatibility", "Compatibility (OpenGL 3.3 — Lite / Weak Hardware)"),
		("forward_plus",     "Forward+ (Vulkan / D3D12 — High End Desktop)"),
		("mobile",           "Mobile (Vulkan Mobile — Balanced)")
	};

	public static void PopulateRenderingMethods(OptionButton optionBtn)
	{
		if (optionBtn == null) return;
		optionBtn.Clear();
		for (int i = 0; i < RenderingMethodOptions.Length; i++)
		{
			optionBtn.AddItem(RenderingMethodOptions[i].Label, i);
		}
	}

	public static string GetActiveGPUInfo()
	{
		string adapter = RenderingServer.GetVideoAdapterName();
		if (string.IsNullOrWhiteSpace(adapter)) adapter = "Standard Graphics Adapter";
		string currentMethod = RenderingServer.GetCurrentRenderingMethod();
		string friendlyMethod = currentMethod switch
		{
			"gl_compatibility" => "Compatibility (OpenGL 3.3)",
			"forward_plus" => "Forward+ (Vulkan)",
			"mobile" => "Mobile (Vulkan)",
			_ => currentMethod
		};
		return $"System: {adapter}  |  Active Engine Backend: {friendlyMethod}";
	}

	public static void ApplyRenderingBackend(string method, SceneTree tree = null)
	{
		var s = SaveManager.CurrentData?.Audio;
		if (s != null)
		{
			s.RenderingMethod = method;
			SaveManager.SaveDirect();
		}

		try
		{
			ProjectSettings.SetSetting("rendering/renderer/rendering_method", method);
			var cfg = new ConfigFile();
			if (FileAccess.FileExists("override.cfg"))
			{
				cfg.Load("override.cfg");
			}
			cfg.SetValue("rendering", "renderer/rendering_method", method);
			cfg.Save("override.cfg");
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[SettingsBinder] Error updating override.cfg: {ex.Message}");
		}

		OS.SetRestartOnExit(true, new string[] { "--rendering-method", method });
	}

	public static void ApplyLitePreset(SceneTree tree = null)
	{
		var s = SaveManager.CurrentData?.Audio;
		if (s == null) return;

		s.LiteMode = true;
		s.ResolutionWidth = 1280;
		s.ResolutionHeight = 720;
		s.AspectScaling = 0;
		s.RenderScale = 0.60f;
		s.ScalingMode = 0; // Bilinear zero overhead
		s.MaxFps = 60;
		s.VSync = true;
		s.RenderingMethod = "gl_compatibility";

		SaveManager.SaveDirect();
		ApplyAllSettings(tree);
		ApplyRenderingBackend("gl_compatibility", tree);
	}

	public static void RestartGameNow(SceneTree tree = null)
	{
		var sceneTree = tree ?? Engine.GetMainLoop() as SceneTree;
		string method = SaveManager.CurrentData?.Audio?.RenderingMethod ?? "gl_compatibility";
		OS.SetRestartOnExit(true, new string[] { "--rendering-method", method });
		sceneTree?.Quit();
	}

	public static void PopulateMaxFps(OptionButton optionBtn)
	{
		if (optionBtn == null) return;
		optionBtn.Clear();
		for (int i = 0; i < MaxFpsOptions.Length; i++)
		{
			optionBtn.AddItem(MaxFpsOptions[i].Label, i);
		}
	}

	public static void PopulateScalingModes(OptionButton optionBtn)
	{
		if (optionBtn == null) return;
		optionBtn.Clear();
		for (int i = 0; i < ScalingModeOptions.Length; i++)
		{
			optionBtn.AddItem(ScalingModeOptions[i].Label, i);
		}
	}

	public static void ApplyEffective3DRenderScale(SceneTree tree = null)
	{
		var sceneTree = tree ?? Engine.GetMainLoop() as SceneTree;
		if (sceneTree?.Root == null) return;

		var s = SaveManager.CurrentData?.Audio;
		if (s == null) return;

		int targetW = s.ResolutionWidth > 0 ? s.ResolutionWidth : 1920;
		float userScale = Mathf.Clamp(s.RenderScale, 0.25f, 1.0f);
		int scalingMode = s.ScalingMode;

		var currentMode = DisplayServer.WindowGetMode();
		bool isFullscreen = currentMode == DisplayServer.WindowMode.Fullscreen || currentMode == DisplayServer.WindowMode.ExclusiveFullscreen;

		float effectiveScale = userScale;
		if (isFullscreen)
		{
			Vector2I screenSize = DisplayServer.ScreenGetSize();
			int screenW = screenSize.X > 0 ? screenSize.X : 1920;
			float resRatio = Mathf.Clamp((float)targetW / (float)screenW, 0.20f, 1.0f);
			effectiveScale = Mathf.Clamp(resRatio * userScale, 0.15f, 1.0f);
		}

		bool isForwardPlus = RenderingServer.GetCurrentRenderingMethod() == "forward_plus";
		sceneTree.Root.Scaling3DMode = (scalingMode == 1 && isForwardPlus)
			? Viewport.Scaling3DModeEnum.Fsr 
			: Viewport.Scaling3DModeEnum.Bilinear;
		sceneTree.Root.Scaling3DScale = effectiveScale;
	}

	public static void ApplyRenderScale(float scale, int scalingMode, SceneTree tree = null)
	{
		var sceneTree = tree ?? Engine.GetMainLoop() as SceneTree;
		scale = Mathf.Clamp(scale, 0.25f, 1.0f);

		SaveManager.CurrentData.Audio.RenderScale = scale;
		SaveManager.CurrentData.Audio.ScalingMode = scalingMode;
		SaveManager.SaveDirect();

		ApplyEffective3DRenderScale(sceneTree);
	}

	public static void ApplyMaxFps(int maxFps)
	{
		Engine.MaxFps = maxFps;
		SaveManager.CurrentData.Audio.MaxFps = maxFps;
		SaveManager.SaveDirect();
	}

	public static void ApplyFpsCounter(bool show, SceneTree tree = null)
	{
		var sceneTree = tree ?? Engine.GetMainLoop() as SceneTree;
		if (sceneTree != null)
		{
			FpsOverlay.EnsureInstance(sceneTree);
			FpsOverlay.Instance?.SetOverlayVisible(show);
		}
		SaveManager.CurrentData.Audio.ShowFpsCounter = show;
		SaveManager.SaveDirect();
	}

	public static void ApplyResolution(int width, int height, SceneTree tree = null)
	{
		var sceneTree = tree ?? Engine.GetMainLoop() as SceneTree;
		if (sceneTree?.Root != null)
		{
			sceneTree.Root.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
			sceneTree.Root.ContentScaleSize = new Vector2I(1920, 1080);
		}

		SaveManager.CurrentData.Audio.ResolutionWidth = width;
		SaveManager.CurrentData.Audio.ResolutionHeight = height;
		SaveManager.SaveDirect();

		var currentMode = DisplayServer.WindowGetMode();
		bool isFullscreen = (SaveManager.CurrentData?.Audio?.Fullscreen == true) ||
		                    currentMode == DisplayServer.WindowMode.Fullscreen ||
		                    currentMode == DisplayServer.WindowMode.ExclusiveFullscreen;

		if (!isFullscreen)
		{
			if (currentMode == DisplayServer.WindowMode.Maximized)
			{
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			}

			DisplayServer.WindowSetSize(new Vector2I(width, height));
			Vector2I screen = DisplayServer.ScreenGetSize();
			DisplayServer.WindowSetPosition(new Vector2I(
				Mathf.Max(0, (screen.X - width) / 2),
				Mathf.Max(0, (screen.Y - height) / 2)
			));
		}

		ApplyEffective3DRenderScale(sceneTree);
	}

	public static void ApplyAspectScaling(int mode, SceneTree tree = null)
	{
		var sceneTree = tree ?? Engine.GetMainLoop() as SceneTree;
		if (sceneTree?.Root != null)
		{
			sceneTree.Root.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
			sceneTree.Root.ContentScaleSize = new Vector2I(1920, 1080);

			Window.ContentScaleAspectEnum aspect = mode switch
			{
				0 => Window.ContentScaleAspectEnum.Expand,
				1 => Window.ContentScaleAspectEnum.Keep,
				2 => Window.ContentScaleAspectEnum.KeepWidth,
				3 => Window.ContentScaleAspectEnum.KeepHeight,
				_ => Window.ContentScaleAspectEnum.Keep
			};
			sceneTree.Root.ContentScaleAspect = aspect;
		}
		SaveManager.CurrentData.Audio.AspectScaling = mode;
		SaveManager.SaveDirect();
	}

	public static void ApplyFullscreen(bool isFullscreen, SceneTree tree = null)
	{
		var sceneTree = tree ?? Engine.GetMainLoop() as SceneTree;
		int w = SaveManager.CurrentData?.Audio?.ResolutionWidth ?? 1920;
		int h = SaveManager.CurrentData?.Audio?.ResolutionHeight ?? 1080;
		if (w <= 0 || h <= 0) { w = 1920; h = 1080; }

		SaveManager.CurrentData.Audio.Fullscreen = isFullscreen;
		SaveManager.SaveDirect();

		if (isFullscreen)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		}
		else
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			DisplayServer.WindowSetSize(new Vector2I(w, h));
			Vector2I screen = DisplayServer.ScreenGetSize();
			DisplayServer.WindowSetPosition(new Vector2I(
				Mathf.Max(0, (screen.X - w) / 2),
				Mathf.Max(0, (screen.Y - h) / 2)
			));
		}

		if (sceneTree?.Root != null)
		{
			sceneTree.Root.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
			sceneTree.Root.ContentScaleSize = new Vector2I(1920, 1080);
		}

		ApplyEffective3DRenderScale(sceneTree);
	}

	public static void ApplyVSync(bool isVSync)
	{
		DisplayServer.WindowSetVsyncMode(isVSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
		SaveManager.CurrentData.Audio.VSync = isVSync;
		SaveManager.SaveDirect();
	}

	public static void ApplyFov(float fov)
	{
		SaveManager.CurrentData.Audio.Fov = fov;
		if (CameraPivot.Instance != null && CameraPivot.Instance.GetNodeOrNull<Camera3D>("Camera3D") is { } cam)
		{
			cam.Fov = fov;
		}
		SaveManager.SaveDirect();
	}

	public static void ApplyMouseSensitivity(float sens)
	{
		SaveManager.CurrentData.Audio.MouseSensitivity = sens;
		if (CameraPivot.Instance != null)
		{
			CameraPivot.Instance.MouseSensitivity = sens;
		}
		SaveManager.SaveDirect();
	}

	public static void ApplyInvertY(bool invert)
	{
		SaveManager.CurrentData.Audio.InvertY = invert;
		if (CameraPivot.Instance != null)
		{
			CameraPivot.Instance.InvertY = invert;
		}
		SaveManager.SaveDirect();
	}

	public static void ApplyHeadbob(bool enabled)
	{
		SaveManager.CurrentData.Audio.Headbob = enabled;
		if (CameraPivot.Instance != null)
		{
			CameraPivot.Instance.EnableWalkWobble = enabled;
		}
		SaveManager.SaveDirect();
	}

	public static void ApplyScreenShake(bool enabled)
	{
		SaveManager.CurrentData.Audio.ScreenShake = enabled;
		if (CameraPivot.Instance != null)
		{
			CameraPivot.Instance.EnableAttackWobble = enabled;
		}
		SaveManager.SaveDirect();
	}

	public static void ApplyConfirmExitActions(bool enabled)
	{
		SaveManager.CurrentData.Audio.ConfirmExitActions = enabled;
		SaveManager.SaveDirect();
	}

	public static void ApplyAllSettings(SceneTree tree)
	{
		var s = SaveManager.CurrentData?.Audio;
		if (s == null) return;

		AudioManager.Instance?.SetMasterVolume(s.MasterVolume, false);
		AudioManager.Instance?.SetMusicVolume(s.MusicVolume, false);
		AudioManager.Instance?.SetSFXVolume(s.SFXVolume, false);

		int w = s.ResolutionWidth > 0 ? s.ResolutionWidth : 1920;
		int h = s.ResolutionHeight > 0 ? s.ResolutionHeight : 1080;

		if (tree?.Root != null)
		{
			tree.Root.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
			tree.Root.ContentScaleSize = new Vector2I(1920, 1080);
		}

		ApplyResolution(w, h, tree);
		ApplyFullscreen(s.Fullscreen, tree);
		ApplyAspectScaling(s.AspectScaling, tree);
		ApplyVSync(s.VSync);
		ApplyRenderScale(s.RenderScale, s.ScalingMode, tree);
		ApplyMaxFps(s.MaxFps);
		ApplyFpsCounter(s.ShowFpsCounter, tree);
		CameraPivot.Instance?.ApplySavedSettings();
	}

	public static void BindFullUI(
		SceneTree tree,
		Button audioTabBtn, Button displayTabBtn, Button controlsTabBtn,
		Control audioPanel, Control displayPanel, Control controlsPanel,
		HSlider masterSlider, Label masterLabel,
		HSlider musicSlider, Label musicLabel,
		HSlider sfxSlider, Label sfxLabel,
		OptionButton resOption, OptionButton aspectOption,
		CheckBox fullscreenCheck, CheckBox vsyncCheck,
		HSlider renderScaleSlider, Label renderScaleLabel,
		OptionButton scalingModeOption,
		OptionButton maxFpsOption,
		CheckBox showFpsCheck,
		HSlider fovSlider, Label fovLabel,
		HSlider sensSlider, Label sensLabel,
		CheckBox invertYCheck, CheckBox headbobCheck, CheckBox shakeCheck,
		Button resetDefaultsBtn,
		OptionButton backendOption = null,
		Button applyLitePresetBtn = null,
		Label gpuInfoLabel = null,
		Control restartNoticeBox = null,
		Button restartNowBtn = null,
		CheckBox confirmExitCheck = null)
	{
		PopulateResolutions(resOption);
		PopulateAspectModes(aspectOption);
		PopulateScalingModes(scalingModeOption);
		PopulateMaxFps(maxFpsOption);
		if (backendOption != null) PopulateRenderingMethods(backendOption);

		void SwitchTab(int tab)
		{
			if (audioPanel != null) audioPanel.Visible = (tab == 0);
			if (displayPanel != null) displayPanel.Visible = (tab == 1);
			if (controlsPanel != null) controlsPanel.Visible = (tab == 2);
		}

		if (audioTabBtn != null) audioTabBtn.Pressed += () => SwitchTab(0);
		if (displayTabBtn != null) displayTabBtn.Pressed += () => SwitchTab(1);
		if (controlsTabBtn != null) controlsTabBtn.Pressed += () => SwitchTab(2);
		SwitchTab(0);

		if (masterSlider != null)
		{
			masterSlider.ValueChanged += (val) =>
			{
				if (masterLabel != null) masterLabel.Text = $"{Mathf.RoundToInt(val)}%";
				if (_isSyncingUI) return;
				AudioManager.Instance?.SetMasterVolume((float)val / 100.0f);
			};
		}
		if (musicSlider != null)
		{
			musicSlider.ValueChanged += (val) =>
			{
				if (musicLabel != null) musicLabel.Text = $"{Mathf.RoundToInt(val)}%";
				if (_isSyncingUI) return;
				AudioManager.Instance?.SetMusicVolume((float)val / 100.0f);
			};
		}
		if (sfxSlider != null)
		{
			sfxSlider.ValueChanged += (val) =>
			{
				if (sfxLabel != null) sfxLabel.Text = $"{Mathf.RoundToInt(val)}%";
				if (_isSyncingUI) return;
				AudioManager.Instance?.SetSFXVolume((float)val / 100.0f);
			};
		}

		if (resOption != null)
		{
			resOption.ItemSelected += (idx) =>
			{
				if (_isSyncingUI) return;
				if (idx >= 0 && idx < SupportedResolutions.Length)
				{
					ApplyResolution(SupportedResolutions[idx].Width, SupportedResolutions[idx].Height, tree);
				}
			};
		}
		if (aspectOption != null)
		{
			aspectOption.ItemSelected += (idx) =>
			{
				if (_isSyncingUI) return;
				if (idx >= 0 && idx < AspectModes.Length)
				{
					ApplyAspectScaling(AspectModes[idx].Mode, tree);
				}
			};
		}
		if (fullscreenCheck != null)
		{
			fullscreenCheck.Toggled += (toggled) => { if (!_isSyncingUI) ApplyFullscreen(toggled, tree); };
		}
		if (vsyncCheck != null)
		{
			vsyncCheck.Toggled += (toggled) => { if (!_isSyncingUI) ApplyVSync(toggled); };
		}
		if (renderScaleSlider != null)
		{
			renderScaleSlider.ValueChanged += (val) =>
			{
				if (renderScaleLabel != null) renderScaleLabel.Text = $"{Mathf.RoundToInt(val)}%";
				if (_isSyncingUI) return;
				float scale = (float)val / 100.0f;
				int mode = SaveManager.CurrentData?.Audio?.ScalingMode ?? 0;
				ApplyRenderScale(scale, mode, tree);
			};
		}
		if (scalingModeOption != null)
		{
			scalingModeOption.ItemSelected += (idx) =>
			{
				if (_isSyncingUI) return;
				if (idx >= 0 && idx < ScalingModeOptions.Length)
				{
					float scale = SaveManager.CurrentData?.Audio?.RenderScale ?? 1.0f;
					ApplyRenderScale(scale, ScalingModeOptions[idx].Mode, tree);
				}
			};
		}
		if (maxFpsOption != null)
		{
			maxFpsOption.ItemSelected += (idx) =>
			{
				if (_isSyncingUI) return;
				if (idx >= 0 && idx < MaxFpsOptions.Length)
				{
					ApplyMaxFps(MaxFpsOptions[idx].Fps);
				}
			};
		}
		if (showFpsCheck != null)
		{
			showFpsCheck.Toggled += (toggled) => { if (!_isSyncingUI) ApplyFpsCounter(toggled, tree); };
		}
		if (fovSlider != null)
		{
			fovSlider.ValueChanged += (val) =>
			{
				if (fovLabel != null) fovLabel.Text = $"{Mathf.RoundToInt(val)}°";
				if (_isSyncingUI) return;
				ApplyFov((float)val);
			};
		}

		if (sensSlider != null)
		{
			sensSlider.ValueChanged += (val) =>
			{
				float sens = (float)val / 10000.0f;
				if (sensLabel != null) sensLabel.Text = $"{sens:F4}";
				if (_isSyncingUI) return;
				ApplyMouseSensitivity(sens);
			};
		}
		if (invertYCheck != null)
		{
			invertYCheck.Toggled += (toggled) => { if (!_isSyncingUI) ApplyInvertY(toggled); };
		}
		if (headbobCheck != null)
		{
			headbobCheck.Toggled += (toggled) => { if (!_isSyncingUI) ApplyHeadbob(toggled); };
		}
		if (shakeCheck != null)
		{
			shakeCheck.Toggled += (toggled) => { if (!_isSyncingUI) ApplyScreenShake(toggled); };
		}
		if (confirmExitCheck != null)
		{
			confirmExitCheck.Toggled += (toggled) => { if (!_isSyncingUI) ApplyConfirmExitActions(toggled); };
		}

		if (backendOption != null)
		{
			backendOption.ItemSelected += (idx) =>
			{
				if (_isSyncingUI) return;
				if (idx >= 0 && idx < RenderingMethodOptions.Length)
				{
					string chosen = RenderingMethodOptions[idx].Method;
					ApplyRenderingBackend(chosen, tree);
					string current = RenderingServer.GetCurrentRenderingMethod();
					if (restartNoticeBox != null)
					{
						restartNoticeBox.Visible = (chosen != current);
					}
				}
			};
		}

		if (applyLitePresetBtn != null)
		{
			applyLitePresetBtn.Pressed += () =>
			{
				ApplyLitePreset(tree);
				SyncFullUI(
					masterSlider, masterLabel,
					musicSlider, musicLabel,
					sfxSlider, sfxLabel,
					resOption, aspectOption,
					fullscreenCheck, vsyncCheck,
					renderScaleSlider, renderScaleLabel,
					scalingModeOption, maxFpsOption, showFpsCheck,
					fovSlider, fovLabel,
					sensSlider, sensLabel,
					invertYCheck, headbobCheck, shakeCheck,
					backendOption, gpuInfoLabel, restartNoticeBox,
					confirmExitCheck
				);
				string chosen = SaveManager.CurrentData?.Audio?.RenderingMethod ?? "gl_compatibility";
				string current = RenderingServer.GetCurrentRenderingMethod();
				if (restartNoticeBox != null)
				{
					restartNoticeBox.Visible = (chosen != current);
				}
			};
		}

		if (restartNowBtn != null)
		{
			restartNowBtn.Pressed += () => RestartGameNow(tree);
		}

		if (resetDefaultsBtn != null)
		{
			resetDefaultsBtn.Pressed += () =>
			{
				ResetToDefaults(tree);
				SyncFullUI(
					masterSlider, masterLabel,
					musicSlider, musicLabel,
					sfxSlider, sfxLabel,
					resOption, aspectOption,
					fullscreenCheck, vsyncCheck,
					renderScaleSlider, renderScaleLabel,
					scalingModeOption, maxFpsOption, showFpsCheck,
					fovSlider, fovLabel,
					sensSlider, sensLabel,
					invertYCheck, headbobCheck, shakeCheck,
					backendOption, gpuInfoLabel, restartNoticeBox,
					confirmExitCheck
				);
			};
		}

		SyncFullUI(
			masterSlider, masterLabel,
			musicSlider, musicLabel,
			sfxSlider, sfxLabel,
			resOption, aspectOption,
			fullscreenCheck, vsyncCheck,
			renderScaleSlider, renderScaleLabel,
			scalingModeOption, maxFpsOption, showFpsCheck,
			fovSlider, fovLabel,
			sensSlider, sensLabel,
			invertYCheck, headbobCheck, shakeCheck,
			backendOption, gpuInfoLabel, restartNoticeBox,
			confirmExitCheck
		);
	}

	public static void ResetToDefaults(SceneTree tree)
	{
		var s = SaveManager.CurrentData?.Audio;
		if (s == null) return;

		s.MasterVolume = 1.0f;
		s.MusicVolume = 0.8f;
		s.SFXVolume = 1.0f;
		s.Fullscreen = false;
		s.ResolutionWidth = 1920;
		s.ResolutionHeight = 1080;
		s.AspectScaling = 0;
		s.VSync = true;
		s.RenderScale = 1.0f;
		s.ScalingMode = 0;
		s.MaxFps = 60;
		s.ShowFpsCounter = false;
		s.RenderingMethod = "forward_plus";
		s.LiteMode = false;
		s.Fov = 75.0f;
		s.MouseSensitivity = 0.0035f;
		s.InvertY = false;
		s.Headbob = true;
		s.ScreenShake = true;
		s.ConfirmExitActions = true;

		SaveManager.SaveDirect();
		ApplyAllSettings(tree);
	}

	public static void SyncFullUI(
		HSlider masterSlider, Label masterLabel,
		HSlider musicSlider, Label musicLabel,
		HSlider sfxSlider, Label sfxLabel,
		OptionButton resOption, OptionButton aspectOption,
		CheckBox fullscreenCheck, CheckBox vsyncCheck,
		HSlider renderScaleSlider, Label renderScaleLabel,
		OptionButton scalingModeOption,
		OptionButton maxFpsOption,
		CheckBox showFpsCheck,
		HSlider fovSlider, Label fovLabel,
		HSlider sensSlider, Label sensLabel,
		CheckBox invertYCheck, CheckBox headbobCheck, CheckBox shakeCheck,
		OptionButton backendOption = null,
		Label gpuInfoLabel = null,
		Control restartNoticeBox = null,
		CheckBox confirmExitCheck = null)
	{
		_isSyncingUI = true;
		try
		{
			var s = SaveManager.CurrentData?.Audio;
			if (s == null) return;

			if (backendOption != null)
			{
				int selIdx = 0;
				for (int i = 0; i < RenderingMethodOptions.Length; i++)
				{
					if (RenderingMethodOptions[i].Method == s.RenderingMethod)
					{
						selIdx = i;
						break;
					}
				}
				backendOption.Select(selIdx);
			}

			if (gpuInfoLabel != null)
			{
				gpuInfoLabel.Text = GetActiveGPUInfo();
			}

			if (restartNoticeBox != null)
			{
				string current = RenderingServer.GetCurrentRenderingMethod();
				restartNoticeBox.Visible = (s.RenderingMethod != current);
			}

			if (masterSlider != null) masterSlider.Value = s.MasterVolume * 100.0f;
			if (musicSlider != null) musicSlider.Value = s.MusicVolume * 100.0f;
			if (sfxSlider != null) sfxSlider.Value = s.SFXVolume * 100.0f;
			if (masterLabel != null) masterLabel.Text = $"{Mathf.RoundToInt(s.MasterVolume * 100.0f)}%";
			if (musicLabel != null) musicLabel.Text = $"{Mathf.RoundToInt(s.MusicVolume * 100.0f)}%";
			if (sfxLabel != null) sfxLabel.Text = $"{Mathf.RoundToInt(s.SFXVolume * 100.0f)}%";

			if (resOption != null)
			{
				int selIdx = 0;
				for (int i = 0; i < SupportedResolutions.Length; i++)
				{
					if (SupportedResolutions[i].Width == s.ResolutionWidth && SupportedResolutions[i].Height == s.ResolutionHeight)
					{
						selIdx = i;
						break;
					}
				}
				resOption.Select(selIdx);
			}
			if (aspectOption != null)
			{
				aspectOption.Select(Mathf.Clamp(s.AspectScaling, 0, AspectModes.Length - 1));
			}
			if (fullscreenCheck != null) fullscreenCheck.ButtonPressed = s.Fullscreen;
			if (vsyncCheck != null) vsyncCheck.ButtonPressed = s.VSync;

			if (renderScaleSlider != null) renderScaleSlider.Value = s.RenderScale * 100.0f;
			if (renderScaleLabel != null) renderScaleLabel.Text = $"{Mathf.RoundToInt(s.RenderScale * 100.0f)}%";
			if (scalingModeOption != null)
			{
				int selIdx = 0;
				for (int i = 0; i < ScalingModeOptions.Length; i++)
				{
					if (ScalingModeOptions[i].Mode == s.ScalingMode) { selIdx = i; break; }
				}
				scalingModeOption.Select(selIdx);
			}
			if (maxFpsOption != null)
			{
				int selIdx = 0;
				for (int i = 0; i < MaxFpsOptions.Length; i++)
				{
					if (MaxFpsOptions[i].Fps == s.MaxFps) { selIdx = i; break; }
				}
				maxFpsOption.Select(selIdx);
			}
			if (showFpsCheck != null) showFpsCheck.ButtonPressed = s.ShowFpsCounter;

			if (fovSlider != null) fovSlider.Value = s.Fov;
			if (fovLabel != null) fovLabel.Text = $"{Mathf.RoundToInt(s.Fov)}°";

			if (sensSlider != null) sensSlider.Value = s.MouseSensitivity * 10000.0f;
			if (sensLabel != null) sensLabel.Text = $"{s.MouseSensitivity:F4}";
			if (invertYCheck != null) invertYCheck.ButtonPressed = s.InvertY;
			if (headbobCheck != null) headbobCheck.ButtonPressed = s.Headbob;
			if (shakeCheck != null) shakeCheck.ButtonPressed = s.ScreenShake;
			if (confirmExitCheck != null) confirmExitCheck.ButtonPressed = s.ConfirmExitActions;
		}
		finally
		{
			_isSyncingUI = false;
		}
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
