// =========================================================================================
// Original Author : air (Airless-KV) - Nov 15, 2026
// Responsibilities: - Roblox-style animated 3D objective waypoint arrow (bobbing & spinning)
//                   - 2D screen-space edge clamping arrow pointer for off-screen targets
//                   - Real-time distance measurement in meters and dynamic target tracking
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 15, 2026 | air : Implemented 3D bobbing waypoint arrow and 2D off-screen edge pointer
// =========================================================================================
using Godot;
using System;

public partial class TutorialWaypointArrow : Node3D
{
	[ExportGroup("3D Visuals")]
	[Export] public bool Enable3DArrow = true;
	[Export] public float BobFrequency = 3.5f;
	[Export] public float BobHeight = 0.35f;
	[Export] public float SpinSpeed = 2.0f;
	[Export] public Vector3 HeightOffset = new(0, 2.5f, 0);

	[ExportGroup("2D Screen Pointer")]
	[Export] public Control ScreenPointerControl;
	[Export] public Label DistanceLabel;
	[Export] public Control ArrowVisual;
	[Export] public float ScreenMargin = 70.0f;

	public Vector3 CurrentTargetPos { get; private set; }
	public bool HasTarget { get; private set; } = false;
	public string CurrentObjectiveName { get; private set; } = "";

	private Node3D _arrowMeshRoot;
	private float _animTimer = 0f;
	private Camera3D _camera;

	public override void _Ready()
	{
		Build3DArrowMesh();
	}

	private void Build3DArrowMesh()
	{
		if (!Enable3DArrow) return;

		_arrowMeshRoot = new Node3D { Name = "ArrowMeshRoot" };
		AddChild(_arrowMeshRoot);

		// Bright emissive gold/yellow material
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1.0f, 0.85f, 0.2f),
			EmissionEnabled = true,
			Emission = new Color(1.0f, 0.8f, 0.15f),
			EmissionEnergyMultiplier = 2.0f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled
		};

		// Arrow Shaft (Cylinder)
		var shaftMesh = new CylinderMesh
		{
			TopRadius = 0.18f,
			BottomRadius = 0.18f,
			Height = 0.8f,
			Material = mat
		};
		var shaft = new MeshInstance3D
		{
			Mesh = shaftMesh,
			Position = new Vector3(0, 0.6f, 0)
		};
		_arrowMeshRoot.AddChild(shaft);

		// Arrow Tip (Cone pointing down)
		var tipMesh = new CylinderMesh
		{
			TopRadius = 0.0f,
			BottomRadius = 0.45f,
			Height = 0.6f,
			Material = mat
		};
		var tip = new MeshInstance3D
		{
			Mesh = tipMesh,
			Position = new Vector3(0, 0.0f, 0)
		};
		_arrowMeshRoot.AddChild(tip);

		_arrowMeshRoot.Visible = false;
	}

	public void SetTarget(Vector3 worldPos, string objectiveName)
	{
		CurrentTargetPos = worldPos;
		CurrentObjectiveName = objectiveName;
		HasTarget = true;
		Visible = true;

		if (_arrowMeshRoot != null)
			_arrowMeshRoot.Visible = true;

		if (ScreenPointerControl != null)
			ScreenPointerControl.Visible = true;
	}

	public void ClearTarget()
	{
		HasTarget = false;
		if (_arrowMeshRoot != null)
			_arrowMeshRoot.Visible = false;

		if (ScreenPointerControl != null)
			ScreenPointerControl.Visible = false;
	}

	public override void _Process(double delta)
	{
		if (!HasTarget)
		{
			if (ScreenPointerControl != null && ScreenPointerControl.Visible)
				ScreenPointerControl.Visible = false;
			return;
		}

		float dt = (float)delta;
		_animTimer += dt;

		// 1. Animate 3D World Arrow
		float bobY = Mathf.Sin(_animTimer * BobFrequency) * BobHeight;
		GlobalPosition = CurrentTargetPos + HeightOffset + new Vector3(0, bobY, 0);

		if (_arrowMeshRoot != null)
		{
			_arrowMeshRoot.RotateY(SpinSpeed * dt);
		}

		// 2. Animate 2D Screen-Space Pointer
		UpdateScreenPointer();
	}

	private void UpdateScreenPointer()
	{
		if (ScreenPointerControl == null) return;

		_camera ??= GetViewport()?.GetCamera3D();
		if (_camera == null || !_camera.Current)
		{
			ScreenPointerControl.Visible = false;
			return;
		}

		Vector3 targetPos = CurrentTargetPos + HeightOffset * 0.5f;
		float dist = _camera.GlobalPosition.DistanceTo(targetPos);

		if (DistanceLabel != null)
		{
			DistanceLabel.Text = $"{CurrentObjectiveName} • {Mathf.RoundToInt(dist)}m";
		}

		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		Vector2 screenCenter = viewportSize * 0.5f;

		bool isBehind = _camera.IsPositionBehind(targetPos);
		Vector2 unprojected = _camera.UnprojectPosition(targetPos);

		// If behind camera, invert vector from screen center
		if (isBehind)
		{
			Vector2 fromCenter = unprojected - screenCenter;
			unprojected = screenCenter - fromCenter.Normalized() * (Mathf.Max(viewportSize.X, viewportSize.Y));
		}

		Rect2 bounds = new(
			ScreenMargin,
			ScreenMargin,
			viewportSize.X - ScreenMargin * 2f,
			viewportSize.Y - ScreenMargin * 2f
		);

		bool isOffScreen = isBehind || !bounds.HasPoint(unprojected);

		if (isOffScreen)
		{
			// Clamp to screen edge borders
			Vector2 dir = (unprojected - screenCenter).Normalized();
			Vector2 clampedPos = screenCenter;

			// Raycast from center to edge of screen rectangle
			float halfW = (viewportSize.X * 0.5f) - ScreenMargin;
			float halfH = (viewportSize.Y * 0.5f) - ScreenMargin;

			float tX = dir.X != 0f ? (Mathf.Sign(dir.X) * halfW) / dir.X : float.MaxValue;
			float tY = dir.Y != 0f ? (Mathf.Sign(dir.Y) * halfH) / dir.Y : float.MaxValue;
			float t = Mathf.Min(Mathf.Abs(tX), Mathf.Abs(tY));

			clampedPos = screenCenter + dir * t;
			ScreenPointerControl.GlobalPosition = clampedPos - ScreenPointerControl.Size * 0.5f;
			ScreenPointerControl.Visible = true;

			// Point arrow towards the off-screen target direction
			if (ArrowVisual != null)
			{
				float angle = dir.Angle();
				// Default arrow icon points Down (+PI/2). Rotate accordingly.
				ArrowVisual.Rotation = angle - Mathf.Pi * 0.5f;
			}
		}
		else
		{
			// On-screen: float directly above target
			Vector2 pos = unprojected - new Vector2(ScreenPointerControl.Size.X * 0.5f, ScreenPointerControl.Size.Y + 25f);
			ScreenPointerControl.GlobalPosition = pos;
			ScreenPointerControl.Visible = true;

			// On-screen: arrow points straight down at objective
			if (ArrowVisual != null)
			{
				ArrowVisual.Rotation = 0f;
			}
		}
	}
}