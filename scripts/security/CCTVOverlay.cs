// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - CCTV security camera monitor on-screen display (OSD) overlay
//                   - Camera channel tags (CAM 01–04) and feed switching synchronization
//                   - Live ticking security timestamp and blinking [REC] indicator
//                   - Two-row bottom OSD layout matching visual layout specifications
//                   - CRT scanlines and subtle camera vignetting management
//                   - Green phosphor night vision mode switching and status synchronization
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented CCTV monitor overlay with scanlines, timestamp, and REC dot
// - Oct 30, 2026 | air : Aligned OSD layout with exact two-row specifications and corner viewfinder frame
// - Oct 31, 2026 | air : Exposed ChannelNames as public static single source of truth for CCTV feeds
// - Oct 31, 2026 | air : Compacted timer updates and node resolution
// - Nov 10, 2026 | air : Updated OSD controls label for Left Click switch and Right Click exit
// - Nov 13, 2026 | air : Integrated green phosphor night vision shader parameter controls and OSD indicator
// - Nov 14, 2026 | air : Corrected channel names for CAM 02 (East Perimeter) and CAM 03 (South Porch & Door)
// - Nov 14, 2026 | air : Implemented TriggerCameraGlitch for CRT analog static bursts and entity vanish
// =========================================================================================
using Godot;
using System;

public partial class CCTVOverlay : CanvasLayer
{
	[ExportGroup("UI References")]
	[Export] public Label ChannelLabel;
	[Export] public Label RecDot;
	[Export] public Label RecText;
	[Export] public Label TimestampLabel;
	[Export] public Label ControlsLabel;
	[Export] public Label StatusLabel;
	[Export] public Label ExitLabel;
	[Export] public ColorRect ScanlinesRect;

	public static readonly string[] ChannelNames =
	{
		"CAM 01 - NORTH PERIMETER",
		"CAM 02 - EAST PERIMETER",
		"CAM 03 - SOUTH PORCH & DOOR",
		"CAM 04 - WEST BOUNDARY"
	};

	private int _currentChannel;
	private float _recTimer;
	private float _clockTimer = 1.0f;
	private bool _isNightVision = true;
	private Tween _glitchTween;

	public bool IsNightVision => _isNightVision;

	public override void _Ready()
	{
		Layer = 60;
		ChannelLabel ??= GetNodeOrNull<Label>("Container/Header/ChannelLabel");
		RecDot ??= GetNodeOrNull<Label>("Container/Header/RecContainer/RecDot");
		RecText ??= GetNodeOrNull<Label>("Container/Header/RecContainer/RecText");
		TimestampLabel ??= GetNodeOrNull<Label>("Container/Footer/LeftCol/TimestampLabel");
		ControlsLabel ??= GetNodeOrNull<Label>("Container/Footer/LeftCol/ControlsLabel");
		StatusLabel ??= GetNodeOrNull<Label>("Container/Footer/RightCol/StatusLabel");
		ExitLabel ??= GetNodeOrNull<Label>("Container/Footer/RightCol/ExitLabel");
		ScanlinesRect ??= GetNodeOrNull<ColorRect>("Container/Scanlines");

		SetNightVision(true);
		SetChannel(0);
		UpdateTimestamp();
		Visible = false;
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;
		float dt = (float)delta;

		_recTimer += dt;
		if (RecDot != null) RecDot.Visible = (Mathf.FloorToInt(_recTimer * 2f) % 2) == 0;

		_clockTimer += dt;
		if (_clockTimer >= 1f) { _clockTimer = 0f; UpdateTimestamp(); }
	}

	public void ShowOverlay(int channelIndex = 0)
	{
		Visible = true;
		SetNightVision(_isNightVision);
		SetChannel(channelIndex);
		UpdateTimestamp();
	}

	public void HideOverlay()
	{
		Visible = false;
		if (_glitchTween != null && _glitchTween.IsValid())
			_glitchTween.Kill();
		if (ScanlinesRect?.Material is ShaderMaterial mat)
			mat.SetShaderParameter("glitch_intensity", 0.0f);
	}

	public void TriggerCameraGlitch(float duration = 0.65f, Action onPeak = null)
	{
		if (ScanlinesRect?.Material is not ShaderMaterial mat)
		{
			onPeak?.Invoke();
			return;
		}

		if (_glitchTween != null && _glitchTween.IsValid())
			_glitchTween.Kill();

		float spikeTime = Mathf.Max(0.08f, duration * 0.28f);
		float decayTime = Mathf.Max(0.12f, duration - spikeTime);

		if (StatusLabel != null)
			StatusLabel.Text = "SIGNAL: 12% | [!] INTERFERENCE DETECTED";

		AudioManager.Sfx("window_rattle", 2.0f);

		_glitchTween = CreateTween();
		// 1. Violent glitch build-up to 1.0 (static snow & tearing)
		_glitchTween.TweenMethod(Callable.From<float>(val => mat.SetShaderParameter("glitch_intensity", val)), 0.0f, 1.0f, spikeTime)
			.SetTrans(Tween.TransitionType.Quad)
			.SetEase(Tween.EaseType.In);

		// 2. Peak glitch: screen is fully covered in static; invoke vanish callback
		_glitchTween.TweenCallback(Callable.From(() =>
		{
			onPeak?.Invoke();
		}));

		// 3. Smooth static decay back to 0.0, revealing the cleared scene
		_glitchTween.TweenMethod(Callable.From<float>(val => mat.SetShaderParameter("glitch_intensity", val)), 1.0f, 0.0f, decayTime)
			.SetTrans(Tween.TransitionType.Quad)
			.SetEase(Tween.EaseType.Out);

		_glitchTween.TweenCallback(Callable.From(() =>
		{
			mat.SetShaderParameter("glitch_intensity", 0.0f);
			UpdateStatusDisplay();
		}));
	}

	public void SetNightVision(bool enabled)
	{
		_isNightVision = enabled;
		if (ScanlinesRect?.Material is ShaderMaterial mat)
		{
			mat.SetShaderParameter("night_vision_enabled", enabled);
		}
		UpdateStatusDisplay();
	}

	public void ToggleNightVision() => SetNightVision(!_isNightVision);

	public void SetChannel(int channelIndex)
	{
		_currentChannel = Mathf.Clamp(channelIndex, 0, ChannelNames.Length - 1);
		if (ChannelLabel != null) ChannelLabel.Text = ChannelNames[_currentChannel];
		UpdateStatusDisplay();
		if (ControlsLabel != null) ControlsLabel.Text = "[L-CLICK / TAB] SWITCH CAM  [V] NVG";
		if (ExitLabel != null) ExitLabel.Text = "[R-CLICK / ESC] EXIT DESK";
	}

	private void UpdateStatusDisplay()
	{
		if (StatusLabel != null)
		{
			string nvgTag = _isNightVision ? "NVG: ON" : "NVG: OFF";
			StatusLabel.Text = $"SIGNAL: 98% | {nvgTag} | 30 FPS";
		}
	}

	private void UpdateTimestamp()
	{
		if (TimestampLabel != null)
			TimestampLabel.Text = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt").ToUpper();
	}
}
