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

	private readonly string[] _channelNames =
	{
		"CAM 01 - NORTH PERIMETER",
		"CAM 02 - EAST PORCH & DOOR",
		"CAM 03 - SOUTH FIELD",
		"CAM 04 - WEST BOUNDARY"
	};

	private int _currentChannel = 0;
	private float _recTimer = 0.0f;
	private float _clockTimer = 1.0f;

	public override void _Ready()
	{
		Layer = 60; // Render above 3D world, below jumpscare/pause screens

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

		// Blinking REC dot (1 Hz)
		_recTimer += dt;
		if (RecDot != null)
		{
			RecDot.Visible = (Mathf.FloorToInt(_recTimer * 2.0f) % 2) == 0;
		}

		// Update live timestamp every second
		_clockTimer += dt;
		if (_clockTimer >= 1.0f)
		{
			_clockTimer = 0.0f;
			UpdateTimestamp();
		}
	}

	public void ShowOverlay(int channelIndex = 0)
	{
		Visible = true;
		SetChannel(channelIndex);
		UpdateTimestamp();
	}

	public void HideOverlay()
	{
		Visible = false;
	}

	public void SetChannel(int channelIndex)
	{
		_currentChannel = Mathf.Clamp(channelIndex, 0, _channelNames.Length - 1);
		if (ChannelLabel != null)
		{
			ChannelLabel.Text = _channelNames[_currentChannel];
		}

		if (StatusLabel != null)
		{
			StatusLabel.Text = "SIGNAL: 98% | 30 FPS";
		}

		if (ControlsLabel != null)
		{
			ControlsLabel.Text = "[1-4 / TAB] SWITCH CAMERA";
		}

		if (ExitLabel != null)
		{
			ExitLabel.Text = "[E / N] EXIT DESK";
		}
	}

	private void UpdateTimestamp()
	{
		if (TimestampLabel != null)
		{
			TimestampLabel.Text = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt").ToUpper();
		}
	}
}
