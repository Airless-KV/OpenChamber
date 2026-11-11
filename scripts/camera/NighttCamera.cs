// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - CCTV security camera rig manager (North, East, South, West static feeds)
//                   - Camera feed switching (1-4, Tab) and mode toggling
//                   - CCTV monitoring integration with BreachManager
//                   - CCTV surveillance monitor OSD overlay synchronization
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Integrated SetCCTVActive callbacks with CameraPivot for mouse capture handling
// - Oct 24, 2026 | air : Streamlined camera switching array logic and timer connections for Alpha build
// - Oct 30, 2026 | air : Integrated camera feed HUD identification labels with CCTV monitor switching
// - Oct 30, 2026 | air : Integrated CCTVOverlay (scanlines, channel tags, timestamp, REC indicator)
// - Oct 31, 2026 | air : Removed legacy prototype timer code; restricted N/E hotkeys to exit feed mode
// - Oct 31, 2026 | air : Consolidated channel names to reference CCTVOverlay.ChannelNames
// - Oct 31, 2026 | air : Streamlined camera switching array logic and mode transitions
// - Nov 10, 2026 | air : Added Left Click next camera feed and Right Click exit monitor
// =========================================================================================
using Godot;

public partial class NighttCamera : Node3D
{
	private Camera3D _dayCamera;
	private Camera3D[] _nightCams;
	private Node3D _cameraPivot;
	private HUDManager _hud;
	private int _currentNightIndex;

	public int CurrentNightIndex => _currentNightIndex;
	public Camera3D CurrentNightCamera => (_nightCams != null && _currentNightIndex >= 0 && _currentNightIndex < _nightCams.Length) ? _nightCams[_currentNightIndex] : null;

	[ExportGroup("CCTV Settings")]
	[Export] public bool IsNightMode;
	[Export] public CCTVOverlay OverlayNode;

	public override void _Ready()
	{
		_nightCams = new[] { GetNode<Camera3D>("North"), GetNode<Camera3D>("East"), GetNode<Camera3D>("South"), GetNode<Camera3D>("West") };
		_cameraPivot = GetNodeOrNull<Node3D>("../CameraPivot") ?? GetTree().Root.FindChild("CameraPivot", true, false) as Node3D;
		_dayCamera = _cameraPivot?.GetNodeOrNull<Camera3D>("Camera3D");
		_hud = GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		OverlayNode ??= GetTree().Root.FindChild("CCTVOverlay", true, false) as CCTVOverlay;
		ApplyMode(IsNightMode);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!IsNightMode) return;

		if (@event is InputEventMouseButton { Pressed: true } mb)
		{
			if (mb.ButtonIndex == MouseButton.Left)
			{
				SwitchNightCamera((_currentNightIndex + 1) % _nightCams.Length);
				GetViewport().SetInputAsHandled();
				return;
			}
			else if (mb.ButtonIndex == MouseButton.Right)
			{
				ApplyMode(false);
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventKey { Pressed: true, Echo: false } k)
		{
			if (k.Keycode is Key.N or Key.E) ApplyMode(false);
			else if (k.Keycode is >= Key.Key1 and <= Key.Key4) SwitchNightCamera((int)k.Keycode - (int)Key.Key1);
			else if (k.Keycode == Key.Tab) SwitchNightCamera((_currentNightIndex + 1) % _nightCams.Length);
			else return;

			GetViewport().SetInputAsHandled();
		}
	}

	public void ApplyMode(bool night)
	{
		IsNightMode = night;
		if (_cameraPivot is CameraPivot cp)
		{
			cp.SetProcess(!night);
			cp.SetProcessInput(!night);
			cp.SetCCTVActive(night);
		}

		if (_hud != null) _hud.Visible = !night;

		if (IsNightMode)
		{
			OverlayNode?.ShowOverlay(_currentNightIndex);
			SwitchNightCamera(_currentNightIndex);
		}
		else
		{
			_dayCamera?.MakeCurrent();
			_hud?.ClearInteractionPrompt();
			OverlayNode?.HideOverlay();
		}
	}

	public void SwitchNightCamera(int index)
	{
		if (index < 0 || index >= _nightCams.Length || _nightCams[index] == null) return;
		_currentNightIndex = index;
		_nightCams[_currentNightIndex].MakeCurrent();
		OverlayNode?.SetChannel(_currentNightIndex);

		string camLabel = index < CCTVOverlay.ChannelNames.Length ? CCTVOverlay.ChannelNames[index] : $"CAM 0{index + 1}";
		_hud?.SetInteractionPrompt($"[{camLabel}] - [1-4 / Tab] Switch Feeds | [N / E] Exit Feed");
	}
}
