using Godot;

public partial class PlayerController : CharacterBody3D
{
	[Signal]
	public delegate void LocationChangedEventHandler(bool isIndoors);

	[ExportGroup("Movement")]
	[Export] public float WalkSpeed = 6.0f;
	[Export] public float SprintSpeed = 9.5f;
	[Export] public float Acceleration = 15.0f;
	[Export] public float Deceleration = 20.0f;
	[Export] public float TurnSpeed = 12.0f;
	[Export] public float Gravity = 19.6f;

	[ExportGroup("Visuals")]
	[Export] public Node3D VisualRoot;

	[ExportGroup("Boundaries")]
	[Export] public Vector3 HouseCenter = new Vector3(-1.66f, 1.2f, 0.0f);
	[Export] public Vector3 HouseExtents = new Vector3(3.5f, 3.0f, 3.5f);

	public bool IsIndoors { get; private set; } = false;

	private bool _wasIndoors = false;

	public override void _Ready()
	{
		AddToGroup("Player");
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		Vector3 vel = Velocity;

		// Apply gravity
		if (!IsOnFloor())
		{
			vel.Y -= Gravity * dt;
		}
		else
		{
			vel.Y = -0.1f; // Keep grounded firmly
		}

		// Read WASD and Arrow key input
		Vector2 inputDir = Vector2.Zero;
		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) inputDir.Y -= 1.0f;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) inputDir.Y += 1.0f;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) inputDir.X -= 1.0f;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) inputDir.X += 1.0f;

		// Support Godot InputMap actions if defined
		if (Input.IsActionPressed("ui_up") && inputDir.Y == 0) inputDir.Y -= 1.0f;
		if (Input.IsActionPressed("ui_down") && inputDir.Y == 0) inputDir.Y += 1.0f;
		if (Input.IsActionPressed("ui_left") && inputDir.X == 0) inputDir.X -= 1.0f;
		if (Input.IsActionPressed("ui_right") && inputDir.X == 0) inputDir.X += 1.0f;

		inputDir = inputDir.Normalized();

		// Calculate world direction based on Camera view
		Camera3D camera = GetViewport().GetCamera3D();
		Vector3 moveDir = Vector3.Zero;

		if (camera != null)
		{
			Vector3 camFwd = -camera.GlobalTransform.Basis.Z;
			camFwd.Y = 0;
			camFwd = camFwd.Normalized();

			Vector3 camRight = camera.GlobalTransform.Basis.X;
			camRight.Y = 0;
			camRight = camRight.Normalized();

			moveDir = (camFwd * (-inputDir.Y) + camRight * inputDir.X).Normalized();
		}
		else
		{
			moveDir = new Vector3(inputDir.X, 0, inputDir.Y).Normalized();
		}

		// Speed & Sprint
		bool isSprinting = Input.IsKeyPressed(Key.Shift);
		float targetSpeed = isSprinting ? SprintSpeed : WalkSpeed;

		if (moveDir.LengthSquared() > 0.001f)
		{
			vel.X = Mathf.MoveToward(vel.X, moveDir.X * targetSpeed, Acceleration * dt);
			vel.Z = Mathf.MoveToward(vel.Z, moveDir.Z * targetSpeed, Acceleration * dt);

			// Rotate visual model towards movement direction
			if (VisualRoot != null)
			{
				float targetAngle = Mathf.Atan2(moveDir.X, moveDir.Z);
				float currentAngle = VisualRoot.Rotation.Y;
				VisualRoot.Rotation = new Vector3(
					VisualRoot.Rotation.X,
					Mathf.LerpAngle(currentAngle, targetAngle, TurnSpeed * dt),
					VisualRoot.Rotation.Z
				);
			}
		}
		else
		{
			vel.X = Mathf.MoveToward(vel.X, 0, Deceleration * dt);
			vel.Z = Mathf.MoveToward(vel.Z, 0, Deceleration * dt);
		}

		Velocity = vel;
		MoveAndSlide();

		CheckLocationState();
	}

	private void CheckLocationState()
	{
		Vector3 diff = GlobalPosition - HouseCenter;
		bool indoors = Mathf.Abs(diff.X) <= HouseExtents.X &&
					   Mathf.Abs(diff.Z) <= HouseExtents.Z &&
					   Mathf.Abs(diff.Y) <= HouseExtents.Y;

		if (indoors != _wasIndoors)
		{
			_wasIndoors = indoors;
			IsIndoors = indoors;
			EmitSignal(SignalName.LocationChanged, IsIndoors);
			GD.Print($"Player location changed: {(IsIndoors ? "INDOORS (Safe inside house)" : "OUTDOORS (In garden/farm)")}");
		}
	}
}
