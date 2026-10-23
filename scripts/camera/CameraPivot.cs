using Godot;

public partial class CameraPivot : Node3D
{
	[ExportGroup("Target Follow")]
	[Export] public Node3D Target;
	[Export] public float FollowSpeed = 10.0f;
	[Export] public Vector3 FollowOffset = Vector3.Zero;

	[ExportGroup("Orbit & Pitch")]
	[Export] public float MouseSensitivity = 0.005f;
	[Export] public float KeyboardTurnSpeed = 3.0f;
	[Export] public bool HoldClickToRotate = true;
	[Export] public float MinPitchDegrees = -70.0f; // Limit looking up
	[Export] public float MaxPitchDegrees = 20.0f;  // Limit looking down

	[ExportGroup("Zoom Settings")]
	[Export] public float ZoomStep = 1.0f;          // Distance shifted per wheel notch
	[Export] public float MinZoom = 2.0f;           // Closest camera distance
	[Export] public float MaxZoom = 50.0f;          // Farthest camera distance
	[Export] public float ZoomSmoothSpeed = 10.0f;  // Interpolation speed

	private Camera3D _camera;
	private bool _isPressed = false;
	private float _pitch;
	private float _yaw;
	private float _targetZoom;

	public override void _Ready()
	{
		// Cache the child Camera3D
		_camera = GetNodeOrNull<Camera3D>("Camera3D");

		if (Target == null)
		{
			Target = GetTree().Root.FindChild("Player", true, false) as Node3D;
		}

		// Initialize angles from current node rotation
		_yaw = Rotation.Y;
		_pitch = Rotation.X;

		// Initialize target zoom based on the camera's starting Z position
		if (_camera != null)
		{
			_targetZoom = Mathf.Clamp(_camera.Position.Z, MinZoom, MaxZoom);
			_camera.Position = new Vector3(_camera.Position.X, _camera.Position.Y, _targetZoom);
		}
	}

	public override void _Input(InputEvent @event)
	{
		// 1. Mouse Button Handling (Right-Click or Left-Click to orbit + Mouse Wheel Zoom)
		if (@event is InputEventMouseButton mouseBtn)
		{
			// Right-click is preferred for orbit so left-click is free for interaction/farming
			if (mouseBtn.ButtonIndex == MouseButton.Right || mouseBtn.ButtonIndex == MouseButton.Middle)
			{
				_isPressed = mouseBtn.Pressed;
			}
			else if (mouseBtn.ButtonIndex == MouseButton.Left && !HoldClickToRotate)
			{
				_isPressed = mouseBtn.Pressed;
			}
			else if (mouseBtn.ButtonIndex == MouseButton.WheelUp && mouseBtn.Pressed)
			{
				// Zoom In
				_targetZoom = Mathf.Clamp(_targetZoom - ZoomStep, MinZoom, MaxZoom);
			}
			else if (mouseBtn.ButtonIndex == MouseButton.WheelDown && mouseBtn.Pressed)
			{
				// Zoom Out
				_targetZoom = Mathf.Clamp(_targetZoom + ZoomStep, MinZoom, MaxZoom);
			}
		}

		// 2. Mouse Drag Orbit & Tilt
		if (@event is InputEventMouseMotion mouseMotion)
		{
			if (!HoldClickToRotate || _isPressed)
			{
				// Horizontal yaw
				_yaw -= mouseMotion.Relative.X * MouseSensitivity;

				// Vertical pitch (inverted so dragging up tilts up)
				_pitch -= mouseMotion.Relative.Y * MouseSensitivity;

				// Clamp pitch between min and max radians
				float minPitchRad = Mathf.DegToRad(MinPitchDegrees);
				float maxPitchRad = Mathf.DegToRad(MaxPitchDegrees);
				_pitch = Mathf.Clamp(_pitch, minPitchRad, maxPitchRad);

				ApplyRotation();
			}
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		// 1. Follow Target (Player) if assigned
		if (Target != null)
		{
			Vector3 targetPos = Target.GlobalPosition + FollowOffset;
			GlobalPosition = GlobalPosition.Lerp(targetPos, FollowSpeed * dt);
		}

		// 2. Keyboard Horizontal Controls (Q/E or Arrow Keys)
		float horizontal = 0f;
		if (Input.IsKeyPressed(Key.Q) || Input.IsKeyPressed(Key.Left))
		{
			horizontal -= 1.0f;
		}
		if (Input.IsKeyPressed(Key.E) && Target == null) // E rotates camera only if no player interaction target
		{
			// let universal interaction have E priority
		}
		if (Input.IsKeyPressed(Key.Right))
		{
			horizontal += 1.0f;
		}

		if (Mathf.Abs(horizontal) > 0.001f)
		{
			_yaw += horizontal * KeyboardTurnSpeed * dt;
			ApplyRotation();
		}

		// 3. Smoothly Interpolate Camera Zoom Distance
		if (_camera != null)
		{
			Vector3 camPos = _camera.Position;
			camPos.Z = Mathf.Lerp(camPos.Z, _targetZoom, ZoomSmoothSpeed * dt);
			_camera.Position = camPos;
		}
	}

	private void ApplyRotation()
	{
		// Apply Euler rotation: Pitch on X, Yaw on Y
		Rotation = new Vector3(_pitch, _yaw, 0f);
	}
}
