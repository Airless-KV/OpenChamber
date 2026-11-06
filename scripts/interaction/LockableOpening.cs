using Godot;

// Ikabit bilang DIREKTANG ANAK ng FrontDoor / NorthWindow
// (yung may InteractableDoor / InteractableWindow script).
//
// F (hawakan):
//   - may plank    = tanggalin ang mga plank (ibabalik sa inventory)
//   - walang plank = i-lock / i-unlock (kailangang sarado)
// G = maglagay ng plank (puwede kahit bukas o sarado ang pinto)
//
// Awtomatikong inilalagay ang mga plank sa bukana, ayon sa laki ng DoorMesh / WindowMesh.
public partial class LockableOpening : Node3D
{
	[Export] public float InteractRange = 4.0f;
	[Export] public float LookDot = 0.7f;
	[Export] public float HoldSeconds = 1.2f;
	[Export] public int MaxPlanks = 3;
	[Export(PropertyHint.Enum, "North,East,South,West")] public int AttackDirection = 0;

	// Kung nasa maling gilid ang mga kahoy, palitan ang PlankSide ng -1.
	[Export] public float PlankSide = 1.0f;
	[Export] public float PlankGap = 0.05f;
	[Export] public Vector3 BlockerSize = new Vector3(1.4f, 2.2f, 0.5f);

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

		// Ilipat ang mga plank sa labas ng pinto para hindi umikot kapag binuksan.
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

			float height = sizeY;
			Vector3 up = Vector3.Up;
			Vector3 xAxis = widthAxis.Normalized();
			Vector3 zAxis = xAxis.Cross(up).Normalized();
			blockBasis = new Basis(xAxis, up, zAxis);
			blockSize = new Vector3(width, height, 0.5f);

			int n = PlankVisuals.Length;
			float bottom = center.Y - height / 2f;
			Vector3 depthOffset = zAxis * PlankSide * (thickness / 2f + PlankGap);

			for (int i = 0; i < n; i++)
			{
				Node3D p = PlankVisuals[i];
				if (p == null)
					continue;

				float y = bottom + height * (i + 1) / (n + 1);
				Vector3 pos = new Vector3(center.X, y, center.Z) + depthOffset;
				p.GlobalTransform = new Transform3D(blockBasis, pos);

				float meshW = 1.0f;
				MeshInstance3D pm = p as MeshInstance3D;
				if (pm != null && pm.Mesh != null)
					meshW = pm.Mesh.GetAabb().Size.X;
				if (meshW < 0.01f)
					meshW = 1.0f;

				p.Scale = new Vector3(width * 1.1f / meshW, 1.0f, 1.0f);
			}
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

			if (newParent != null)
				CreateBlocker(center, blockBasis, blockSize, newParent);
		}

		_setupDone = true;
		UpdateVisuals();
	}

	private void CreateBlocker(Vector3 center, Basis basis, Vector3 size, Node parent)
	{
		StaticBody3D body = new StaticBody3D();
		body.Name = "BarricadeBlocker";

		BoxShape3D box = new BoxShape3D();
		box.Size = size;

		_blockerShape = new CollisionShape3D();
		_blockerShape.Shape = box;
		_blockerShape.Disabled = true;

		body.AddChild(_blockerShape);
		parent.AddChild(body);
		body.GlobalTransform = new Transform3D(basis, center);
	}

	public override void _Process(double delta)
	{
		if (_msgTimer > 0f)
			_msgTimer -= (float)delta;

		UpdateVisuals();

		if (_entrance == null || !IsPlayerLooking())
		{
			_held = 0f;
			_toggled = false;
			SetStatus("");
			return;
		}

		HandleLockHold(delta);
		HandleBarricade();
	}

	private void HandleLockHold(double delta)
	{
		if (!Input.IsActionPressed("lock_hold"))
		{
			_held = 0f;
			_toggled = false;

			if (PlanksPlaced > 0)
				SetStatus("BARRICADED (" + PlanksPlaced + "/" + MaxPlanks + ")   [F] hold: remove planks   [G]: add plank");
			else if (IsLocked)
				SetStatus("LOCKED");
			else
				SetStatus("[F] hold: lock   [G]: barricade");
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

		int percent = Mathf.Min(100, (int)(_held / HoldSeconds * 100));
		SetStatus(verb + "... " + percent + "%");

		if (_held < HoldSeconds)
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

	private void HandleBarricade()
	{
		if (!Input.IsActionJustPressed("place_barricade"))
			return;

		if (PlanksPlaced >= MaxPlanks)
		{
			ShowMessage("Barricade full");
			return;
		}

		if (_gm != null && _gm.TryUsePlank())
		{
			PlanksPlaced++;

			if (!_entrance.IsOpen && !_entrance.IsLocked)
			{
				_entrance.IsLocked = true;
				_autoLocked = true;
			}

			AudioManager.Sfx("door_thud", -4f);
			ShowMessage("Plank placed (" + PlanksPlaced + "/" + MaxPlanks + ")");
		}
		else
		{
			ShowMessage("No planks! Buy at the shop.");
		}
	}

	// Para sa BreachManager. False = naantala ang halimaw. True = nakapasok na.
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

	private bool IsPlayerLooking()
	{
		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null)
			return false;

		Vector3 toMe = GlobalPosition - cam.GlobalPosition;
		if (toMe.Length() > InteractRange)
			return false;

		Vector3 forward = -cam.GlobalTransform.Basis.Z;
		return forward.Dot(toMe.Normalized()) >= LookDot;
	}

	private void UpdateVisuals()
	{
		if (LockVisual != null)
			LockVisual.Visible = IsLocked;

		if (!_setupDone)
			return;

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
		if (_blockerShape != null && wantBlock != _blockerOn)
		{
			_blockerOn = wantBlock;
			_blockerShape.SetDeferred("disabled", !wantBlock);
		}
	}

	// Maliit na animation para makita mong nilalagay ang kahoy.
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
		if (_msgTimer > 0f)
			return;

		if (StatusLabel != null)
			StatusLabel.Text = text;
	}

	private void ShowMessage(string text)
	{
		_msgTimer = 1.5f;
		if (StatusLabel != null)
			StatusLabel.Text = text;
	}
}
