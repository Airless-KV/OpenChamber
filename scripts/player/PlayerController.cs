// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - 3D CharacterBody3D player movement, sprint acceleration, and deceleration
//                   - First-person camera-relative movement direction calculation (WASD)
//                   - Player indoor/outdoor boundary detection (IsIndoors safe zone)
//                   - Step-assist and obstacle climbing for door thresholds, curbs, and humps
//                   - Slope climbing with normal projection and vertical incline propulsion
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined movement input vectoring and indoor boundary checks for Alpha build
// - Oct 30, 2026 | air : Aligned movement vectors directly with first-person camera forward and lateral axes
// - Nov 09, 2026 | air : Added step-assist, floor snapping (0.45m), and 60° slope angle for climbing humps
// - Nov 10, 2026 | air : Increased MaxStepHeight to 0.85m for terrain step-assist onto chessboard
// - Nov 11, 2026 | air : Added slope vector projection and 70° incline climbing for steep ramps and terrain
// - Nov 11, 2026 | air : Adjusted house boundary center and extents for elevated treehouse coordinates
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
	[Export] public float MaxStepHeight = 0.85f;
	[Export] public float MaxSlopeAngle = 70.0f;
	[Export] public bool EnableSlopeClimbing = true;

	[ExportGroup("Visuals")]
	[Export] public Node3D VisualRoot;

	[ExportGroup("Boundaries")]
	[Export] public Vector3 HouseCenter = new(-1.66f, 4.6f, 0.0f);
	[Export] public Vector3 HouseExtents = new(3.2f, 2.0f, 3.2f);

	public bool IsIndoors { get; private set; } = false;
	private bool _wasIndoors = false;

	public override void _Ready()
	{
		AddToGroup("Player");
		FloorMaxAngle = Mathf.DegToRad(MaxSlopeAngle);
		FloorSnapLength = 0.5f;
		FloorConstantSpeed = true;
		FloorStopOnSlope = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		Vector3 vel = Velocity;

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

		Vector3 slopeDir = moveDir;
		bool onSlope = false;

		if (EnableSlopeClimbing && IsOnFloor() && moveDir.LengthSquared() > 0.001f)
		{
			Vector3 normal = GetFloorNormal();
			float slopeAngle = normal.AngleTo(Vector3.Up);
			if (slopeAngle > 0.01f && slopeAngle <= FloorMaxAngle)
			{
				onSlope = true;
				// Project horizontal moveDir onto the slope plane surface
				Vector3 tangent = (moveDir - normal * moveDir.Dot(normal)).Normalized();
				slopeDir = tangent;
			}
		}

		if (moveDir.LengthSquared() > 0.001f)
		{
			vel.X = Mathf.MoveToward(vel.X, slopeDir.X * targetSpeed, Acceleration * dt);
			vel.Z = Mathf.MoveToward(vel.Z, slopeDir.Z * targetSpeed, Acceleration * dt);

			if (onSlope)
			{
				// Provide vertical climb/descent velocity along the slope
				vel.Y = Mathf.MoveToward(vel.Y, slopeDir.Y * targetSpeed, Acceleration * dt);
			}
		}
		else
		{
			vel.X = Mathf.MoveToward(vel.X, 0, Deceleration * dt);
			vel.Z = Mathf.MoveToward(vel.Z, 0, Deceleration * dt);

			if (IsOnFloor())
			{
				vel.Y = Mathf.MoveToward(vel.Y, 0, Deceleration * dt);
			}
		}

		if (!onSlope)
		{
			vel.Y = IsOnFloor() ? -0.1f : vel.Y - Gravity * dt;
		}

		// Step-assist for climbing vertical door thresholds, curbs, and sharp steps
		// Only triggers on steep vertical obstacles (normal angle > FloorMaxAngle)
		if (moveDir.LengthSquared() > 0.001f && IsOnFloor())
		{
			Vector3 stepDir = moveDir.Normalized();
			Vector3 stepVector = stepDir * 0.2f;

			var hitCol = new KinematicCollision3D();
			// Check if forward motion hits a vertical step or curb
			if (TestMove(GlobalTransform, stepVector, hitCol))
			{
				float obstacleAngle = hitCol.GetNormal().AngleTo(Vector3.Up);
				// Only step-assist if it's a steep step/wall (not a walkable slope)
				if (obstacleAngle > FloorMaxAngle)
				{
					Vector3 upVector = new(0, MaxStepHeight, 0);
					// Check headroom
					if (!TestMove(GlobalTransform, upVector))
					{
						Transform3D upTransform = GlobalTransform.Translated(upVector);
						// Check forward clearance at elevated step height
						if (!TestMove(upTransform, stepVector))
						{
							// Trace down to detect the step landing
							var downCollision = new KinematicCollision3D();
							if (TestMove(upTransform.Translated(stepVector), -upVector, downCollision))
							{
								float normalAngle = downCollision.GetNormal().AngleTo(Vector3.Up);
								if (normalAngle <= FloorMaxAngle)
								{
									float stepUp = MaxStepHeight + downCollision.GetTravel().Y;
									if (stepUp > 0.005f)
									{
										GlobalPosition += new Vector3(0, stepUp, 0);
									}
								}
							}
						}
					}
				}
			}
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
