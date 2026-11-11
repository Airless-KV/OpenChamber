// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - CCTV security camera monitor on-screen display (OSD) overlay
//                   - Camera channel tags (CAM 01–04) and feed switching synchronization
//                   - Live ticking security timestamp and blinking [REC] indicator
//                   - Two-row bottom OSD layout matching visual layout specifications
//                   - CRT scanlines and subtle camera vignetting management
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 30, 2026 | air : Implemented CCTV monitor overlay with scanlines, timestamp, and REC dot
// - Oct 30, 2026 | air : Aligned OSD layout with exact two-row specifications and corner viewfinder frame
// - Oct 31, 2026 | air : Exposed ChannelNames as public static single source of truth for CCTV feeds
// - Oct 31, 2026 | air : Compacted timer updates and node resolution
// - Nov 10, 2026 | air : Updated OSD controls label for Left Click switch and Right Click exit
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
		"CAM 02 - EAST PORCH & DOOR",
		"CAM 03 - SOUTH FIELD",
		"CAM 04 - WEST BOUNDARY"
	};

	private int _currentChannel;
	private float _recTimer;
	private float _clockTimer = 1.0f;

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

	public void ShowOverlay(int channelIndex = 0) { Visible = true; SetChannel(channelIndex); UpdateTimestamp(); }
	public void HideOverlay() => Visible = false;

	public void SetChannel(int channelIndex)
	{
		_currentChannel = Mathf.Clamp(channelIndex, 0, ChannelNames.Length - 1);
		if (ChannelLabel != null) ChannelLabel.Text = ChannelNames[_currentChannel];
		if (StatusLabel != null) StatusLabel.Text = "SIGNAL: 98% | 30 FPS";
		if (ControlsLabel != null) ControlsLabel.Text = "[L-CLICK / TAB] SWITCH CAMERA";
		if (ExitLabel != null) ExitLabel.Text = "[R-CLICK / ESC] EXIT DESK";
	}

	private void UpdateTimestamp()
	{
		if (TimestampLabel != null)
			TimestampLabel.Text = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt").ToUpper();
	}
}
