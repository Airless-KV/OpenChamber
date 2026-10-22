using Godot;

public partial class NighttCamera : Node3D
{
	private Camera3D _dayCamera;
	private Camera3D _camNorth;
	private Camera3D _camEast;
	private Camera3D _camSouth;
	private Camera3D _camWest;
	private Camera3D[] _nightCams;

	private Node3D _cameraPivot;
	private int _currentNightIndex = 0;

	[Export] public bool IsNightMode = false;
	[Export] public NodePath TimerPath = "../../Timer"; // Path from NightCamera to Timer

	public override void _Ready()
	{
		// 1. Fetch static cameras
		_camNorth = GetNode<Camera3D>("North");
		_camEast  = GetNode<Camera3D>("East");
		_camSouth = GetNode<Camera3D>("South");
		_camWest  = GetNode<Camera3D>("West");

		_nightCams = new Camera3D[] { _camNorth, _camEast, _camSouth, _camWest };

		// 2. Fetch sibling CameraPivot
		_cameraPivot = GetNodeOrNull<Node3D>("../CameraPivot");
		if (_cameraPivot != null)
		{
			_dayCamera = _cameraPivot.GetNodeOrNull<Camera3D>("Camera3D");
		}

		// 3. Connect to the Timer node
		if (HasNode(TimerPath))
		{
			Timer timer = GetNode<Timer>(TimerPath);
			timer.Timeout += OnTimerTimeout;
			GD.Print("Timer successfully connected to NighttCamera.");
		}

		// Initial setup
		ApplyMode(IsNightMode);
	}

	// Called automatically whenever the Timer finishes its countdown
	private void OnTimerTimeout()
	{
		IsNightMode = !IsNightMode;
		ApplyMode(IsNightMode);
		GD.Print("Timer triggered! Switched to: ", IsNightMode ? "NIGHT" : "DAY");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			// Manual toggle override
			if (keyEvent.Keycode == Key.N)
			{
				IsNightMode = !IsNightMode;
				ApplyMode(IsNightMode);
			}

			// Switch camera feeds while in night mode
			if (IsNightMode)
			{
				if (keyEvent.Keycode == Key.Key1) SwitchNightCamera(0);
				if (keyEvent.Keycode == Key.Key2) SwitchNightCamera(1);
				if (keyEvent.Keycode == Key.Key3) SwitchNightCamera(2);
				if (keyEvent.Keycode == Key.Key4) SwitchNightCamera(3);

				if (keyEvent.Keycode == Key.Tab)
				{
					int next = (_currentNightIndex + 1) % _nightCams.Length;
					SwitchNightCamera(next);
				}
			}
		}
	}

	public void ApplyMode(bool night)
	{
		IsNightMode = night;

		if (IsNightMode)
		{
			if (_cameraPivot != null)
			{
				_cameraPivot.SetProcess(false);
				_cameraPivot.SetProcessInput(false);
			}
			SwitchNightCamera(_currentNightIndex);
		}
		else
		{
			if (_cameraPivot != null)
			{
				_cameraPivot.SetProcess(true);
				_cameraPivot.SetProcessInput(true);
			}
			if (_dayCamera != null)
			{
				_dayCamera.MakeCurrent();
			}
		}
	}

	public void SwitchNightCamera(int index)
	{
		if (index < 0 || index >= _nightCams.Length || _nightCams[index] == null)
			return;

		_currentNightIndex = index;
		_nightCams[_currentNightIndex].MakeCurrent();
	}
}
