// =========================================================================================
// Original Author : Robin Velasco (0325-1150@lspu.edu.ph) - Nov 2026
// Responsibilities: - Door & window locking and barricade mechanics
//                   - Automatic plank alignment and sizing across openings
//                   - PvZ Lawnmower entrance sacrifice defense (1 HP)
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 06, 2026 | air : Integrated with BreachManager and standardized headers
// - Nov 06, 2026 | air : Added 3s hold placement/removal sliders, MaxPlanks 5, and TearSinglePlank
// - Nov 06, 2026 | air : Restricted MaxPlanks to 3, implemented Lawnmower 1 HP defense and daytime repair
// =========================================================================================
using Godot;

public partial class LockableOpening : Node3D
{
	[ExportGroup("Interaction Range")]
	[Export] public float InteractRange = 4.0f;
	[Export] public float LookDot = 0.7f;

	[ExportGroup("Barricade Timers & Sliders")]
	[Export(PropertyHint.Range, "0.5,10.0,0.1")] public float PlacePlankSeconds = 3.0f;
	[Export(PropertyHint.Range, "0.5,10.0,0.1")] public float RemovePlankSeconds = 3.0f;
	[Export] public int MaxPlanks = 3;
	[Export(PropertyHint.Enum, "North,East,South,West")] public int AttackDirection = 0;

	[ExportGroup("Entrance Health (Lawnmower Defense)")]
	[Export] public int MaxHealth = 1;
	public int Health { get; set; } = 1;
	public bool IsBroken => Health <= 0;

	public enum DefenseResult { PlankAbsorbed, LawnmowerTriggered, FatalBreach }

	[ExportGroup("Plank Placement Offsets")]
	[Export] public float PlankSide = 1.0f;
	[Export] public float PlankGap = 0.05f;
	[Export] public Vector3 BlockerSize = new Vector3(1.4f, 2.2f, 0.5f);

	[ExportGroup("Visual Bindings")]
	[Export] public Node3D LockVisual;
	[Export] public Node3D[] PlankVisuals;
	[Export] public Label3D StatusLabel;

	public int PlanksPlaced { get; private set; }
	public bool IsLocked => _entrance != null && _entrance.IsLocked;
	public int DefenseLevel => PlanksPlaced + (IsLocked ? 1 : 0);
	public InteractableEntrance Entrance => _entrance;

	private InteractableEntrance _entrance;
	private GameManager _gm;
	private float _held;
	private bool _toggled;
	private float _placeHeld;
	private bool _placeToggled;
	private float _msgTimer;

	private bool _setupDone;
	private bool _autoLocked;
	private bool _blockerOn;
	private Vector3[] _plankScales;
	private CollisionShape3D _blockerShape;

	public override void _Ready()
	{
		_entrance = GetParent() as InteractableEntrance;
		if (_entrance == null)
			GD.PushError("LockableOpening: dapat direktang anak ito ng node na may InteractableDoor o InteractableWindow script.");

		AddToGroup("Lockables");
		_gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;

		if (_gm != null)
		{
			_gm.PhaseChanged += (isDay, _) =>
			{
				if (isDay) RepairEntrance();
			};
		}

		CallDeferred(MethodName.SetupPlanks);
	}

	private MeshInstance3D FindDoorMesh()
	{
		if (_entrance == null)
			return null;

		foreach (Node child in _entrance.GetChildren())
		{
			MeshInstance3D m = child as MeshInstance3D;
			if (m == null)
				continue;

			string n = m.Name.ToString();
			if (n == "DoorMesh" || n == "WindowMesh")
				return m;
		}

		return null;
	}

	private void SetupPlanks()
	{
		Node newParent = null;
		if (_entrance != null)
			newParent = _entrance.GetParent();

		bool hasPlanks = PlankVisuals != null && PlankVisuals.Length > 0;

		if (hasPlanks && newParent != null)
		{
			foreach (Node3D p in PlankVisuals)
			{
				if (p != null && p.GetParent() != newParent)
					p.Reparent(newParent, true);
			}
		}

		Vector3 center = GlobalPosition;
		Basis blockBasis = Basis.Identity;
		Vector3 blockSize = BlockerSize;

		MeshInstance3D doorMesh = FindDoorMesh();
		bool autoLayout = hasPlanks && doorMesh != null && doorMesh.Mesh != null;

		if (autoLayout)
		{
			Aabb box = doorMesh.Mesh.GetAabb();
			Transform3D mt = doorMesh.GlobalTransform;
			Vector3 sc = mt.Basis.Scale;
			center = mt * box.GetCenter();

			float sizeX = box.Size.X * Mathf.Abs(sc.X);
			float sizeY = box.Size.Y * Mathf.Abs(sc.Y);
			float sizeZ = box.Size.Z * Mathf.Abs(sc.Z);

			Basis ob = mt.Basis.Orthonormalized();
			Vector3 widthAxis;
			float width;
			float thickness;

			if (sizeX >= sizeZ)
			{
				widthAxis = ob.X;
				width = sizeX;
				thickness = sizeZ;
			}
			else
			{
				widthAxis = ob.Z;
				width = sizeZ;
				thickness = sizeX;
			}

			Vector3 yAxis = ob.Y;
			Vector3 zAxis = widthAxis.Cross(yAxis).Normalized();
			if (zAxis.LengthSquared() < 0.5f)
				zAxis = ob.Z;

			int n = PlankVisuals.Length;
			float usableH = sizeY * 0.75f;
			Vector3 depthOffset = zAxis * PlankSide * (thickness / 2f + PlankGap);

			for (int i = 0; i < n; i++)
			{
				Node3D p = PlankVisuals[i];
				if (p == null)
					continue;

				float t = (n == 1) ? 0.5f : ((float)i / (n - 1));
				float yOff = (t - 0.5f) * usableH;
				Vector3 pos = center + yAxis * yOff + depthOffset;

				Basis b = new Basis(widthAxis, yAxis, zAxis);
				p.GlobalTransform = new Transform3D(b, pos);
			}

			blockBasis = new Basis(widthAxis, yAxis, zAxis);
			blockSize = new Vector3(width, sizeY, thickness + 0.3f);
		}

		if (hasPlanks)
		{
			_plankScales = new Vector3[PlankVisuals.Length];
			for (int i = 0; i < PlankVisuals.Length; i++)
			{
				if (PlankVisuals[i] != null)
					_plankScales[i] = PlankVisuals[i].Scale;
				else
					_plankScales[i] = Vector3.One;
			}
		}

		CreateBlocker(center, blockBasis, blockSize);
		_setupDone = true;
		UpdateVisuals();
	}

	private void CreateBlocker(Vector3 center, Basis basis, Vector3 size)
	{
		StaticBody3D body = new StaticBody3D();
		body.Name = "BarricadeBlocker";

		_blockerShape = new CollisionShape3D();
		BoxShape3D box = new BoxShape3D();
		box.Size = size;
		_blockerShape.Shape = box;

		body.AddChild(_blockerShape);

		Node p = (_entrance != null) ? _entrance.GetParent() : GetParent();
		if (p != null)
			p.AddChild(body);
		else
			AddChild(body);

		body.GlobalTransform = new Transform3D(basis, center);
		_blockerShape.Disabled = true;
	}

	public override void _Process(double delta)
	{
		if (_msgTimer > 0f)
		{
			_msgTimer -= (float)delta;
			if (_msgTimer <= 0f)
				SetStatus(null);
		}

		UpdateVisuals();

		if (_entrance == null)
			return;

		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null)
			return;

		Vector3 toEntrance = GlobalPosition - cam.GlobalPosition;
		float dist = toEntrance.Length();
		if (dist > InteractRange)
		{
			_held = 0f;
			_toggled = false;
			_placeHeld = 0f;
			_placeToggled = false;
			return;
		}

		Vector3 forward = -cam.GlobalTransform.Basis.Z.Normalized();
		float dot = forward.Dot(toEntrance.Normalized());
		if (dot < LookDot)
		{
			_held = 0f;
			_toggled = false;
			_placeHeld = 0f;
			_placeToggled = false;
			return;
		}

		HandleHold(delta);
		HandleBarricade(delta);
	}

	private void HandleHold(double delta)
	{
		if (!Input.IsActionPressed("lock_hold"))
		{
			_held = 0f;
			_toggled = false;

			if (_placeHeld <= 0f)
			{
				if (PlanksPlaced > 0)
					SetStatus($"BARRICADED ({PlanksPlaced}/{MaxPlanks}) | [F] hold (3s): remove | [G] hold (3s): add plank");
				else if (IsBroken)
					SetStatus($"[BROKEN - 0 HP (LAWNMOWER USED)] | [G] hold (3s): Barricade opening!");
				else if (IsLocked)
					SetStatus("LOCKED | [F] hold (3s): unlock | [G] hold (3s): barricade");
				else
					SetStatus("[F] hold (3s): lock | [G] hold (3s): barricade");
			}
			return;
		}

		if (_toggled)
			return;

		if (PlanksPlaced == 0 && _entrance.IsOpen)
		{
			ShowMessage("Close it first!");
			return;
		}

		_held += (float)delta;

		string verb = "Locking";
		if (PlanksPlaced > 0)
			verb = "Removing planks";
		else if (IsLocked)
			verb = "Unlocking";

		int percent = Mathf.Min(100, (int)(_held / RemovePlankSeconds * 100));
		SetStatus(verb + "... " + percent + "%");

		if (_held < RemovePlankSeconds)
			return;

		_toggled = true;

		if (PlanksPlaced > 0)
		{
			if (_gm != null)
				_gm.RefundPlanks(PlanksPlaced);

			PlanksPlaced = 0;

			if (_autoLocked)
			{
				_entrance.IsLocked = false;
				_autoLocked = false;
			}

			AudioManager.Sfx("door_thud", -4f);
			ShowMessage("Planks removed");
		}
		else
		{
			_entrance.IsLocked = !_entrance.IsLocked;
			AudioManager.Sfx("door_thud");
		}
	}

	private void HandleBarricade(double delta)
	{
		if (!Input.IsActionPressed("place_barricade"))
		{
			_placeHeld = 0f;
			_placeToggled = false;
			return;
		}

		if (_placeToggled)
			return;

		if (PlanksPlaced >= MaxPlanks)
		{
			ShowMessage("Barricade full");
			return;
		}

		_placeHeld += (float)delta;
		int percent = Mathf.Min(100, (int)(_placeHeld / PlacePlankSeconds * 100));
		SetStatus($"Placing plank... {percent}%");

		if (_placeHeld < PlacePlankSeconds)
			return;

		_placeToggled = true;
		_placeHeld = 0f;

		if (_gm != null && _gm.TryUsePlank())
		{
			PlanksPlaced++;

			if (!_entrance.IsOpen && !_entrance.IsLocked)
			{
				_entrance.IsLocked = true;
				_autoLocked = true;
			}

			AudioManager.Sfx("door_thud", -4f);
			ShowMessage($"Plank placed ({PlanksPlaced}/{MaxPlanks})");
		}
		else
		{
			ShowMessage("No planks! Buy at the shop.");
		}
	}

	// Plants vs Zombies Lawnmower defense:
	// If planks exist -> tear 1 plank (absorbed)
	// If 0 planks & Health > 0 (1 HP) -> Lawnmower triggers! Entrance shatters, sacrificing itself to repel Ani-Ani.
	// If 0 planks & Health <= 0 -> Entrance is already broken! Fatal breach!
	public DefenseResult TakeHitAndCheckLawnmower()
	{
		if (PlanksPlaced > 0)
		{
			TearSinglePlank();
			return DefenseResult.PlankAbsorbed;
		}

		if (Health > 0)
		{
			// 1 HP Lawnmower consumed! Entrance shatters to repel Ani-Ani!
			Health = 0;
			if (_entrance != null)
			{
				_entrance.IsLocked = false;
				_entrance.IsOpen = true;
			}
			AudioManager.Sfx("window_rattle", 4f);
			AudioManager.Sfx("door_pound", 3f);
			AudioManager.Sfx("banner_sting");
			ShowMessage("[!] LAWNMOWER DEFENSE! Entrance shattered repelling Ani-Ani! (0 HP - BROKEN!)", 3.0f);
			UpdateVisuals();
			return DefenseResult.LawnmowerTriggered;
		}

		// Already 0 HP and 0 planks: fatal breach!
		return DefenseResult.FatalBreach;
	}

	public void RepairEntrance()
	{
		Health = MaxHealth;
		if (_entrance != null)
		{
			_entrance.IsOpen = false;
			_entrance.IsLocked = true;
		}
		UpdateVisuals();
	}

	// Tears a single plank sequentially during Ani-Ani's assault
	public bool TearSinglePlank()
	{
		if (PlanksPlaced > 0)
		{
			PlanksPlaced--;
			AudioManager.Sfx("window_rattle", 2f);
			AudioManager.Sfx("door_pound", -2f);
			UpdateVisuals();
			return true;
		}
		return false;
	}

	// Legacy breach check for BreachManager
	public bool TryBreach()
	{
		if (PlanksPlaced > 0)
		{
			PlanksPlaced--;
			AudioManager.Sfx("window_rattle");
			return false;
		}

		if (_entrance != null && _entrance.IsLocked)
		{
			_entrance.IsLocked = false;
			_autoLocked = false;
			AudioManager.Sfx("door_thud");
			return false;
		}

		return true;
	}

	public void TryBreakAll()
	{
		PlanksPlaced = 0;
		if (_entrance != null)
			_entrance.IsLocked = false;
		_autoLocked = false;
		UpdateVisuals();
	}

	private void UpdateVisuals()
	{
		if (!_setupDone)
			return;

		if (LockVisual != null)
		{
			bool showLock = IsLocked && PlanksPlaced == 0;
			if (LockVisual.Visible != showLock)
				LockVisual.Visible = showLock;
		}

		if (PlankVisuals != null)
		{
			for (int i = 0; i < PlankVisuals.Length; i++)
			{
				Node3D p = PlankVisuals[i];
				if (p == null)
					continue;

				bool shouldShow = i < PlanksPlaced;
				if (shouldShow && !p.Visible)
				{
					p.Visible = true;
					PopIn(p, i);
				}
				else if (!shouldShow && p.Visible)
				{
					p.Visible = false;
				}
			}
		}

		bool wantBlock = PlanksPlaced > 0;
		if (_blockerShape != null && _blockerOn != wantBlock)
		{
			_blockerShape.Disabled = !wantBlock;
			_blockerOn = wantBlock;
		}
	}

	private void PopIn(Node3D plank, int index)
	{
		Vector3 target = Vector3.One;
		if (_plankScales != null && index < _plankScales.Length)
			target = _plankScales[index];

		plank.Scale = target * 0.2f;

		Tween tw = CreateTween();
		tw.TweenProperty(plank, "scale", target, 0.25f)
			.SetTrans(Tween.TransitionType.Back)
			.SetEase(Tween.EaseType.Out);
	}

	private void SetStatus(string text)
	{
		if (StatusLabel == null)
			return;

		if (string.IsNullOrEmpty(text))
		{
			StatusLabel.Visible = false;
			return;
		}

		StatusLabel.Text = text;
		StatusLabel.Visible = true;
	}

	private void ShowMessage(string text, float duration = 1.6f)
	{
		SetStatus(text);
		_msgTimer = duration;
	}
}
