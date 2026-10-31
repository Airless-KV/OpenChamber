// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - 3D CharacterBody3D player movement, sprint acceleration, and deceleration
//                   - First-person camera-relative movement direction calculation (WASD)
//                   - Player indoor/outdoor boundary detection (IsIndoors safe zone)
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined movement input vectoring and indoor boundary checks for Alpha build
// - Oct 30, 2026 | air : Aligned movement vectors directly with first-person camera forward and lateral axes
// =========================================================================================
using Godot;

public partial class PlayerController : CharacterBody3D
{
	[Signal] public delegate void LocationChangedEventHandler(bool isIndoors);

	[ExportGroup("Movement")]
	[Export] public float WalkSpeed = 6.0f;
	[Export] public float SprintSpeed = 9.5f;
	[Export] public float Acceleration = 15.0f;
	[Export] public float Deceleration = 20.0f;
	[Export] public float Gravity = 19.6f;

	[ExportGroup("Visuals")]
	[Export] public Node3D VisualRoot;

	[ExportGroup("Boundaries")]
	[Export] public Vector3 HouseCenter = new(-1.66f, 1.2f, 0.0f);
	[Export] public Vector3 HouseExtents = new(3.5f, 3.0f, 3.5f);

	public bool IsIndoors { get; private set; } = false;
	private bool _wasIndoors = false;

	public override void _Ready() => AddToGroup("Player");

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		Vector3 vel = Velocity;
		vel.Y = IsOnFloor() ? -0.1f : vel.Y - Gravity * dt;

		Vector2 input = new(
			(Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) || Input.IsActionPressed("ui_right") ? 1f : 0f) -
			(Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) || Input.IsActionPressed("ui_left") ? 1f : 0f),
			(Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down) || Input.IsActionPressed("ui_down") ? 1f : 0f) -
			(Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) || Input.IsActionPressed("ui_up") ? 1f : 0f)
		);
		if (input.LengthSquared() > 0.001f) input = input.Normalized();

		Camera3D cam = GetViewport().GetCamera3D();
		Vector3 moveDir = Vector3.Zero;
		if (cam != null)
		{
			Vector3 fwd = new Vector3(-cam.GlobalTransform.Basis.Z.X, 0, -cam.GlobalTransform.Basis.Z.Z).Normalized();
			Vector3 right = new Vector3(cam.GlobalTransform.Basis.X.X, 0, cam.GlobalTransform.Basis.X.Z).Normalized();
			moveDir = (fwd * (-input.Y) + right * input.X).Normalized();
		}
		else
		{
			moveDir = new Vector3(input.X, 0, input.Y).Normalized();
		}

		float targetSpeed = Input.IsKeyPressed(Key.Shift) ? SprintSpeed : WalkSpeed;

		if (moveDir.LengthSquared() > 0.001f)
		{
			vel.X = Mathf.MoveToward(vel.X, moveDir.X * targetSpeed, Acceleration * dt);
			vel.Z = Mathf.MoveToward(vel.Z, moveDir.Z * targetSpeed, Acceleration * dt);
		}
		else
		{
			vel.X = Mathf.MoveToward(vel.X, 0, Deceleration * dt);
			vel.Z = Mathf.MoveToward(vel.Z, 0, Deceleration * dt);
		}

		Velocity = vel;
		MoveAndSlide();

		Vector3 diff = (GlobalPosition - HouseCenter).Abs();
		bool indoors = diff.X <= HouseExtents.X && diff.Z <= HouseExtents.Z && diff.Y <= HouseExtents.Y;
		if (indoors != _wasIndoors)
		{
			_wasIndoors = IsIndoors = indoors;
			EmitSignal(SignalName.LocationChanged, IsIndoors);
		}
	}
}
