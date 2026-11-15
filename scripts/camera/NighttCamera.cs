// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - CCTV security camera rig manager (North, East, South, West static feeds)
//                   - Camera feed switching (1-4, Tab) and mode toggling
//                   - CCTV monitoring integration with BreachManager
//                   - CCTV surveillance monitor OSD overlay synchronization
//                   - Directional IR illuminator spotlights management for CCTV night vision feed
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
// - Nov 13, 2026 | air : Integrated camera IR illuminator spotlights and [V] key night vision toggle
// - Nov 14, 2026 | air : Aligned CCTV Camera3D rig positions and orientations with physical antenna mast visual assets
// - Nov 14, 2026 | air : Adjusted camera pitch down to 45 deg and FOV to 80 deg for comprehensive porch, side wall, and perimeter coverage
// - Nov 14, 2026 | air : Added Treehouse battery power drain (1% per 10s) and automatic signal cut on battery depletion
// - Nov 14, 2026 | air : Increased CCTV battery power drain to 1% per second
// - Nov 15, 2026 | air : Added FarPlane configuration (280m) to restrict CCTV frustum depth while preserving mountains
// - Nov 15, 2026 | air : Rebalanced CCTV battery drain to 0.5% per second and mapped [SPACE] to emergency siren trigger
// - Nov 15, 2026 | air : Buffed CCTV IRLight to 95m range, 65 deg angle, and Layer 7 mask for complete night coverage
// - Nov 15, 2026 | air : Added static Instance and CCTVModeChanged signal to coordinate entity night stealth visibility
// =========================================================================================
using Godot;

public partial class NighttCamera : Node3D
{
	[Signal] public delegate void CCTVModeChangedEventHandler(bool isCCTVActive);
	public static NighttCamera Instance { get; private set; }

	private Camera3D _dayCamera;
	private Camera3D[] _nightCams;
	private SpotLight3D[] _irLights;
	private Node3D _cameraPivot;
	private HUDManager _hud;
	private int _currentNightIndex;
	private bool _isNightVisionActive = true;
	private float _cctvDrainTimer = 0f;

	public int CurrentNightIndex => _currentNightIndex;
	public Camera3D CurrentNightCamera => (_nightCams != null && _currentNightIndex >= 0 && _currentNightIndex < _nightCams.Length) ? _nightCams[_currentNightIndex] : null;
	public bool IsNightVisionActive => _isNightVisionActive;

	[ExportGroup("CCTV Settings")]
	[Export] public bool IsNightMode;
	[Export] public CCTVOverlay OverlayNode;
	[Export] public float FarPlane = 280.0f;

	public override void _Ready()
	{
		Instance = this;
		_nightCams = new[] { GetNode<Camera3D>("North"), GetNode<Camera3D>("East"), GetNode<Camera3D>("South"), GetNode<Camera3D>("West") };
		_irLights = new SpotLight3D[_nightCams.Length];
		for (int i = 0; i < _nightCams.Length; i++)
		{
			if (_nightCams[i] == null) continue;
			_nightCams[i].Far = FarPlane;
			_irLights[i] = _nightCams[i].GetNodeOrNull<SpotLight3D>("IRLight");
			if (_irLights[i] == null)
			{
				_irLights[i] = new SpotLight3D
				{
					Name = "IRLight",
					LightColor = new Color(0.88f, 0.95f, 1.0f),
					LightEnergy = 3.8f,
					LightCullMask = 7,
					SpotRange = 95.0f,
					SpotAngle = 65.0f,
					SpotAttenuation = 1.0f,
					Visible = false
				};
				_nightCams[i].AddChild(_irLights[i]);
			}
			else
			{
				_irLights[i].LightColor = new Color(0.88f, 0.95f, 1.0f);
				_irLights[i].LightEnergy = 3.8f;
				_irLights[i].LightCullMask = 7;
				_irLights[i].SpotRange = 95.0f;
				_irLights[i].SpotAngle = 65.0f;
				_irLights[i].SpotAttenuation = 1.0f;
				_irLights[i].Visible = false;
			}
		}

		_cameraPivot = GetNodeOrNull<Node3D>("../CameraPivot") ?? GetTree().Root.FindChild("CameraPivot", true, false) as Node3D;
		_dayCamera = _cameraPivot?.GetNodeOrNull<Camera3D>("Camera3D");
		_hud = GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		OverlayNode ??= GetTree().Root.FindChild("CCTVOverlay", true, false) as CCTVOverlay;

		SyncCamerasWithPhysicalAssets();
		ApplyMode(IsNightMode);
	}

	private void SyncCamerasWithPhysicalAssets()
	{
		var houseInterior = GetTree().Root.FindChild("HouseInterior", true, false) as Node3D;
		if (houseInterior == null) return;

		var mast = houseInterior.FindChild("CCTVMast", true, false) as Node3D;
		if (mast == null) return;

		SyncSingleCamera("North", mast.FindChild("CamHeadNorth", true, false) as Node3D);
		SyncSingleCamera("East", mast.FindChild("CamHeadEast", true, false) as Node3D);
		SyncSingleCamera("South", mast.FindChild("CamHeadSouth", true, false) as Node3D);
		SyncSingleCamera("West", mast.FindChild("CamHeadWest", true, false) as Node3D);
	}

	private void SyncSingleCamera(string camNodeName, Node3D visualHead)
	{
		if (visualHead == null) return;
		var cam = GetNodeOrNull<Camera3D>(camNodeName);
		if (cam == null) return;

		var lens = visualHead.GetNodeOrNull<Node3D>($"Lens{camNodeName}") ?? visualHead.GetNodeOrNull<Node3D>("Lens");
		Vector3 targetPos = lens != null ? lens.GlobalPosition : visualHead.GlobalPosition;
		cam.GlobalPosition = targetPos;
		cam.GlobalBasis = visualHead.GlobalBasis;
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
			if (k.Keycode is Key.N or Key.E or Key.Escape) ApplyMode(false);
			else if (k.Keycode is >= Key.Key1 and <= Key.Key4) SwitchNightCamera((int)k.Keycode - (int)Key.Key1);
			else if (k.Keycode == Key.Tab) SwitchNightCamera((_currentNightIndex + 1) % _nightCams.Length);
			else if (k.Keycode == Key.V) { ToggleNightVision(); GetViewport().SetInputAsHandled(); return; }
			else if (k.Keycode == Key.Space) { TriggerSiren(); GetViewport().SetInputAsHandled(); return; }
			else return;

			GetViewport().SetInputAsHandled();
		}
	}

	public void TriggerSiren()
	{
		var breach = GetTree().Root.FindChild("BreachManager", true, false) as BreachManager;
		if (breach == null) return;

		bool success = breach.ActivateSiren(out string message);
		if (OverlayNode != null)
		{
			OverlayNode.ShowAlert(message);
			var station = InteractableBatteryStation.GetStation(PowerSystemType.CCTV)
				?? InteractableBatteryStation.Instance;
			if (station != null && OverlayNode.StatusLabel != null)
			{
				OverlayNode.StatusLabel.Text = $"PWR: {station.BatteryHealth}% [CCTV]";
			}
		}
	}

	public void ToggleNightVision()
	{
		_isNightVisionActive = !_isNightVisionActive;
		UpdateIRLights(_isNightVisionActive ? _currentNightIndex : -1);
		OverlayNode?.SetNightVision(_isNightVisionActive);
	}

	private void UpdateIRLights(int activeIndex)
	{
		if (_irLights == null) return;
		for (int i = 0; i < _irLights.Length; i++)
		{
			if (_irLights[i] != null)
				_irLights[i].Visible = (i == activeIndex);
		}
	}

	public override void _Process(double delta)
	{
		if (!IsNightMode) return;

		var station = InteractableBatteryStation.GetStation(PowerSystemType.CCTV)
			?? InteractableBatteryStation.Instance;
		if (station == null || !station.HasPower)
		{
			// Battery missing or died while watching CCTV
			ApplyMode(false);
			AudioManager.Sfx("banner_sting");
			_hud?.SetInteractionPrompt("[!] CCTV SIGNAL LOST! CCTV PC Battery died or removed (0% HP)!");
			return;
		}

		// CCTV drains at 0.5% per second (1% power every 2.0 seconds)
		_cctvDrainTimer += (float)delta;
		while (_cctvDrainTimer >= 2.0f)
		{
			_cctvDrainTimer -= 2.0f;
			station.ConsumePower(1);
			if (!station.HasPower)
			{
				ApplyMode(false);
				AudioManager.Sfx("banner_sting");
				_hud?.SetInteractionPrompt("[!] CCTV SIGNAL LOST! CCTV PC Battery died (0% HP)!");
				return;
			}
		}

		if (OverlayNode != null && OverlayNode.StatusLabel != null)
		{
			OverlayNode.StatusLabel.Text = $"PWR: {station.BatteryHealth}% [CCTV]";
		}
	}

	public void ApplyMode(bool night)
	{
		IsNightMode = night;
		_cctvDrainTimer = 0f;
		EmitSignal(SignalName.CCTVModeChanged, night);

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

			var station = InteractableBatteryStation.GetStation(PowerSystemType.CCTV)
				?? InteractableBatteryStation.Instance;
			if (station != null && OverlayNode?.StatusLabel != null)
			{
				OverlayNode.StatusLabel.Text = $"PWR: {station.BatteryHealth}% [CCTV]";
			}
		}
		else
		{
			UpdateIRLights(-1);
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
		UpdateIRLights(_isNightVisionActive ? _currentNightIndex : -1);
		OverlayNode?.SetChannel(_currentNightIndex);

		string camLabel = index < CCTVOverlay.ChannelNames.Length ? CCTVOverlay.ChannelNames[index] : $"CAM 0{index + 1}";
		_hud?.SetInteractionPrompt($"[{camLabel}] - [1-4 / Tab] Switch Feeds | [V] NVG | [N / E / R-Click] Exit Feed");
	}
}
