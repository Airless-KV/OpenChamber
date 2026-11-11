// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - Non-hostile ambient farm animal behavior (cows and sheep)
//                   - Ambient wandering, idling, and grazing state machine
//                   - Procedural leg swing, body bobbing, and head tilt chewing animation
//                   - Atmospheric glowing eye illumination creating psychological tension
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 10, 2026 | air : Created AmbientCreature controller for passive red-eyed cows and yellow-eyed sheep
// - Nov 11, 2026 | air : Added 2.5D billboard sprite support with directional FlipH and procedural grazing/idle bobbing
// =========================================================================================
using Godot;
using System;

public partial class AmbientCreature : CharacterBody3D
{
	public enum CreatureState
	{
		Idle,
		Graze,
		Wander
	}

	[ExportGroup("Wander Settings")]
	[Export] public float WalkSpeed = 1.0f;
	[Export] public float WanderRadius = 7.0f;
	[Export] public float MinIdleTime = 3.0f;
	[Export] public float MaxIdleTime = 7.0f;
	[Export] public float MinGrazeTime = 4.0f;
	[Export] public float MaxGrazeTime = 8.0f;
	[Export] public float GrazeChance = 0.5f;

	[ExportGroup("Visual Node Bindings")]
	[Export] public Sprite3D BillboardSprite;
	[Export] public Node3D VisualRoot;
	[Export] public Node3D HeadNode;
	[Export] public Node3D LegFrontLeft;
	[Export] public Node3D LegFrontRight;
	[Export] public Node3D LegBackLeft;
	[Export] public Node3D LegBackRight;
	[Export] public OmniLight3D EyeLight;

	public CreatureState CurrentState { get; private set; } = CreatureState.Idle;

	private Vector3 _homePosition;
	private Vector3 _targetPosition;
	private float _stateTimer;
	private double _animTime;
	private readonly Random _rng = new();
	private float _baseEyeEnergy = 1.2f;

	// Base transforms for clean procedural resets
	private Vector3 _headBaseRot;
	private Vector3 _visualBasePos;
	private Vector3 _spriteBasePos;

	public override void _Ready()
	{
		_homePosition = GlobalPosition;
		_targetPosition = GlobalPosition;

		BillboardSprite ??= GetNodeOrNull<Sprite3D>("Sprite3D");
		if (BillboardSprite != null)
		{
			_spriteBasePos = BillboardSprite.Position;
		}

		// Auto-bind nodes if not explicitly assigned in inspector
		VisualRoot ??= GetNodeOrNull<Node3D>("VisualRoot");
		if (VisualRoot != null)
		{
			_visualBasePos = VisualRoot.Position;
			HeadNode ??= VisualRoot.GetNodeOrNull<Node3D>("HeadNode");
			LegFrontLeft ??= VisualRoot.GetNodeOrNull<Node3D>("FrontLeftLeg");
			LegFrontRight ??= VisualRoot.GetNodeOrNull<Node3D>("FrontRightLeg");
			LegBackLeft ??= VisualRoot.GetNodeOrNull<Node3D>("BackLeftLeg");
			LegBackRight ??= VisualRoot.GetNodeOrNull<Node3D>("BackRightLeg");
		}

		if (HeadNode != null)
		{
			_headBaseRot = HeadNode.RotationDegrees;
			EyeLight ??= HeadNode.GetNodeOrNull<OmniLight3D>("EyeLight");
		}

		if (EyeLight != null)
		{
			_baseEyeEnergy = EyeLight.LightEnergy;
		}

		// Initial idle timer with random offset
		_stateTimer = (float)(MinIdleTime + _rng.NextDouble() * (MaxIdleTime - MinIdleTime));
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		_animTime += delta;
		_stateTimer -= dt;

		// Gentle eye light flicker/pulsing for eerie atmosphere
		if (EyeLight != null)
		{
			float pulse = Mathf.Sin((float)_animTime * 2.8f) * 0.18f;
			EyeLight.LightEnergy = Mathf.Max(0.2f, _baseEyeEnergy + pulse);
		}

		switch (CurrentState)
		{
			case CreatureState.Idle:
				ProcessIdle(dt);
				break;
			case CreatureState.Graze:
				ProcessGraze(dt);
				break;
			case CreatureState.Wander:
				ProcessWander(dt);
				break;
		}
	}

	private void ProcessIdle(float dt)
	{
		// Subtle breathing bob
		if (BillboardSprite != null)
		{
			float breath = Mathf.Sin((float)_animTime * 1.5f) * 0.012f;
			BillboardSprite.Position = new Vector3(_spriteBasePos.X, _spriteBasePos.Y + breath, _spriteBasePos.Z);
		}
		else if (VisualRoot != null)
		{
			float breath = Mathf.Sin((float)_animTime * 1.5f) * 0.02f;
			VisualRoot.Position = new Vector3(_visualBasePos.X, _visualBasePos.Y + breath, _visualBasePos.Z);
		}

		// Return head to level posture
		if (HeadNode != null)
		{
			Vector3 curRot = HeadNode.RotationDegrees;
			HeadNode.RotationDegrees = curRot.Lerp(_headBaseRot, dt * 3.0f);
		}

		// Reset leg rotations smoothly
		ResetLegs(dt);

		if (_stateTimer <= 0f)
		{
			// Decide next state: Graze or Wander
			if (_rng.NextDouble() < GrazeChance)
			{
				EnterGraze();
			}
			else
			{
				EnterWander();
			}
		}
	}

	private void EnterGraze()
	{
		CurrentState = CreatureState.Graze;
		_stateTimer = (float)(MinGrazeTime + _rng.NextDouble() * (MaxGrazeTime - MinGrazeTime));
	}

	private void ProcessGraze(float dt)
	{
		if (BillboardSprite != null)
		{
			float chewBob = Mathf.Abs(Mathf.Sin((float)_animTime * 8.0f)) * 0.02f;
			BillboardSprite.Position = new Vector3(_spriteBasePos.X, _spriteBasePos.Y - 0.04f + chewBob, _spriteBasePos.Z);
		}
		else
		{
			// Tilt head down toward the grass with gentle chewing motion
			if (HeadNode != null)
			{
				float chewPitch = Mathf.Sin((float)_animTime * 9.0f) * 2.5f;
				float chewYaw = Mathf.Cos((float)_animTime * 6.0f) * 2.0f;
				Vector3 targetRot = new(_headBaseRot.X - 26.0f + chewPitch, _headBaseRot.Y + chewYaw, _headBaseRot.Z);
				HeadNode.RotationDegrees = HeadNode.RotationDegrees.Lerp(targetRot, dt * 4.0f);
			}

			if (VisualRoot != null)
			{
				float chewBob = Mathf.Abs(Mathf.Sin((float)_animTime * 9.0f)) * 0.015f;
				VisualRoot.Position = new Vector3(_visualBasePos.X, _visualBasePos.Y - 0.04f + chewBob, _visualBasePos.Z);
			}
		}

		ResetLegs(dt);

		if (_stateTimer <= 0f)
		{
			EnterIdle();
		}
	}

	private void EnterWander()
	{
		// Pick random target position on ground plane around home base
		float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
		float dist = (float)(_rng.NextDouble() * WanderRadius);
		_targetPosition = new Vector3(
			_homePosition.X + Mathf.Cos(angle) * dist,
			GlobalPosition.Y,
			_homePosition.Z + Mathf.Sin(angle) * dist
		);

		CurrentState = CreatureState.Wander;
		// Timeout safety so it doesn't wander forever if path is blocked
		_stateTimer = 12.0f;
	}

	private void ProcessWander(float dt)
	{
		Vector3 diff = _targetPosition - GlobalPosition;
		diff.Y = 0f;
		float distToTarget = diff.Length();

		if (distToTarget <= 0.4f || _stateTimer <= 0f)
		{
			Velocity = Vector3.Zero;
			EnterIdle();
			return;
		}

		Vector3 dir = diff.Normalized();
		Velocity = dir * WalkSpeed;
		MoveAndSlide();

		float walkFreq = 7.5f;

		if (BillboardSprite != null)
		{
			// Flip sprite based on lateral movement direction
			Camera3D cam = GetViewport()?.GetCamera3D();
			if (cam != null)
			{
				Vector3 toCam = (cam.GlobalPosition - GlobalPosition).Normalized();
				Vector3 camRight = Vector3.Up.Cross(toCam).Normalized();
				float dot = dir.Dot(camRight);
				if (Mathf.Abs(dot) > 0.05f)
				{
					BillboardSprite.FlipH = dot < 0;
				}
			}
			else if (Mathf.Abs(dir.X) > 0.05f)
			{
				BillboardSprite.FlipH = dir.X < 0;
			}

			float walkBob = Mathf.Abs(Mathf.Sin((float)_animTime * walkFreq)) * 0.035f;
			BillboardSprite.Position = new Vector3(_spriteBasePos.X, _spriteBasePos.Y + walkBob, _spriteBasePos.Z);
		}
		else
		{
			// Face movement direction smoothly for 3D meshes
			float targetAngle = Mathf.Atan2(dir.X, dir.Z);
			Vector3 currentRot = Rotation;
			currentRot.Y = Mathf.LerpAngle(currentRot.Y, targetAngle, dt * 4.5f);
			Rotation = currentRot;

			// Walking leg swing animation (opposing diagonal legs)
			float swingAngle = 24.0f;
			float legSin = Mathf.Sin((float)_animTime * walkFreq);

			if (LegFrontLeft != null)
				LegFrontLeft.RotationDegrees = new Vector3(legSin * swingAngle, 0, 0);
			if (LegBackRight != null)
				LegBackRight.RotationDegrees = new Vector3(legSin * swingAngle, 0, 0);

			if (LegFrontRight != null)
				LegFrontRight.RotationDegrees = new Vector3(-legSin * swingAngle, 0, 0);
			if (LegBackLeft != null)
				LegBackLeft.RotationDegrees = new Vector3(-legSin * swingAngle, 0, 0);

			// Walking body bob
			if (VisualRoot != null)
			{
				float walkBob = Mathf.Abs(Mathf.Sin((float)_animTime * walkFreq)) * 0.04f;
				VisualRoot.Position = new Vector3(_visualBasePos.X, _visualBasePos.Y + walkBob, _visualBasePos.Z);
			}

			// Keep head natural while walking
			if (HeadNode != null)
			{
				HeadNode.RotationDegrees = HeadNode.RotationDegrees.Lerp(_headBaseRot, dt * 4.0f);
			}
		}
	}

	private void EnterIdle()
	{
		CurrentState = CreatureState.Idle;
		_stateTimer = (float)(MinIdleTime + _rng.NextDouble() * (MaxIdleTime - MinIdleTime));
	}

	private void ResetLegs(float dt)
	{
		float resetSpeed = dt * 6.0f;
		if (LegFrontLeft != null)
			LegFrontLeft.RotationDegrees = LegFrontLeft.RotationDegrees.Lerp(Vector3.Zero, resetSpeed);
		if (LegFrontRight != null)
			LegFrontRight.RotationDegrees = LegFrontRight.RotationDegrees.Lerp(Vector3.Zero, resetSpeed);
		if (LegBackLeft != null)
			LegBackLeft.RotationDegrees = LegBackLeft.RotationDegrees.Lerp(Vector3.Zero, resetSpeed);
		if (LegBackRight != null)
			LegBackRight.RotationDegrees = LegBackRight.RotationDegrees.Lerp(Vector3.Zero, resetSpeed);
	}
}
