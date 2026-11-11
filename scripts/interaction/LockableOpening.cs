// =========================================================================================
// Original Author : rbn (Robin Velasco) - Nov 06, 2026
// Responsibilities: - Bare opening barricade defense mechanics
//                   - Wooden plank placement [G] and removal [F] (3s hold)
//                   - PvZ Lawnmower entrance sacrifice defense (1 HP)
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 06, 2026 | rbn : Implemented bare opening barricade defense mechanics and plank placement/removal
// - Nov 06, 2026 | air : Integrated with BreachManager and standardized headers
// - Nov 06, 2026 | air : Added 3s hold placement/removal sliders, MaxPlanks 5, and TearSinglePlank
// - Nov 06, 2026 | air : Restricted MaxPlanks to 3, implemented Lawnmower 1 HP defense and daytime repair
// - Nov 09, 2026 | air : Converted to pure bare opening defense without doors or windows
// - Nov 10, 2026 | air : Synced AttackDirection with parent InteractableEntrance
// - Nov 10, 2026 | air : Enabled Left Click hold to place planks and Right Click hold to remove planks
// - Nov 10, 2026 | air : Routed barricade prompts and hold progress directly to 2D HUDManager
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
	[Export] public Vector3 BlockerSize = new Vector3(1.6f, 2.2f, 0.4f);

	[ExportGroup("Visual Bindings")]
	[Export] public Node3D LockVisual;
	[Export] public Node3D[] PlankVisuals;
	[Export] public Label3D StatusLabel;

	public int PlanksPlaced { get; private set; }
	public bool IsLocked => false;
	public int DefenseLevel => PlanksPlaced;
	public InteractableEntrance Entrance => _entrance;

	private InteractableEntrance _entrance;
	private GameManager _gm;
	private HUDManager _hud;
	private float _held;
	private bool _toggled;
	private float _placeHeld;
	private bool _placeToggled;
	private float _msgTimer;

	private bool _setupDone;
	private bool _blockerOn;
	private Vector3[] _plankScales;
	private CollisionShape3D _blockerShape;

	public override void _Ready()
	{
		_entrance = GetParent() as InteractableEntrance;
		if (_entrance == null)
		{
			GD.PushError("LockableOpening: must be a child of an InteractableEntrance node.");
		}
		else
		{
			AttackDirection = _entrance.AttackDirection;
		}

		AddToGroup("Lockables");
		_gm = GetTree().Root.FindChild("GameManager", true, false) as GameManager;
		_hud = GetTree().Root.FindChild("HUD", true, false) as HUDManager;

		if (_gm != null)
		{
			_gm.PhaseChanged += (isDay, _) =>
			{
				if (isDay) RepairEntrance();
			};
		}

		CallDeferred(MethodName.SetupPlanks);
	}

	private void SetupPlanks()
	{
		bool hasPlanks = PlankVisuals != null && PlankVisuals.Length > 0;
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

		CreateBlocker(GlobalPosition, GlobalTransform.Basis, BlockerSize);
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
			if (_held > 0f || _placeHeld > 0f)
				SetStatus(null);
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
			if (_held > 0f || _placeHeld > 0f)
				SetStatus(null);
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
		bool isRemovePressed = Input.IsActionPressed("lock_hold") || Input.IsMouseButtonPressed(MouseButton.Right);
		if (!isRemovePressed)
		{
			_held = 0f;
			_toggled = false;

			if (_placeHeld <= 0f)
			{
				if (PlanksPlaced > 0)
					SetStatus($"BARRICADED ({PlanksPlaced}/{MaxPlanks}) | Hold Left Click: Add | Hold Right Click: Remove");
				else if (IsBroken)
					SetStatus("[BROKEN - 0 HP (LAWNMOWER USED)] | Hold Left Click (3s) to Barricade!");
				else
					SetStatus("[BARE OPENING] | Hold Left Click (3s) to Place Plank");
			}
			return;
		}

		if (_toggled)
			return;

		if (PlanksPlaced == 0)
		{
			ShowMessage("Opening is bare - hold Left Click (3s) to place a plank!");
			return;
		}

		_held += (float)delta;
		int percent = Mathf.Min(100, (int)(_held / RemovePlankSeconds * 100));
		SetStatus($"Removing planks... {percent}%");

		if (_held < RemovePlankSeconds)
			return;

		_toggled = true;

		if (PlanksPlaced > 0)
		{
			if (_gm != null)
				_gm.RefundPlanks(PlanksPlaced);

			PlanksPlaced = 0;
			AudioManager.Sfx("door_thud", -4f);
			ShowMessage("Planks removed - opening is bare");
		}
	}

	private void HandleBarricade(double delta)
	{
		bool isPlacePressed = Input.IsActionPressed("place_barricade") || Input.IsMouseButtonPressed(MouseButton.Left);
		if (!isPlacePressed)
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
			AudioManager.Sfx("door_thud", -4f);
			ShowMessage($"Plank placed ({PlanksPlaced}/{MaxPlanks})");
		}
		else
		{
			ShowMessage("No planks! Buy at the shop.");
		}
	}

	// Plants vs Zombies Lawnmower defense:
	public DefenseResult TakeHitAndCheckLawnmower()
	{
		if (PlanksPlaced > 0)
		{
			TearSinglePlank();
			return DefenseResult.PlankAbsorbed;
		}

		if (Health > 0)
		{
			// 1 HP Lawnmower consumed! Frame shatters to repel Ani-Ani!
			Health = 0;
			AudioManager.Sfx("window_rattle", 4f);
			AudioManager.Sfx("door_pound", 3f);
			AudioManager.Sfx("banner_sting");
			ShowMessage("[!] LAWNMOWER DEFENSE! Frame shattered repelling Ani-Ani! (0 HP - BROKEN!)", 3.0f);
			UpdateVisuals();
			return DefenseResult.LawnmowerTriggered;
		}

		// Already 0 HP and 0 planks: fatal breach!
		return DefenseResult.FatalBreach;
	}

	public void RepairEntrance()
	{
		Health = MaxHealth;
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

	public bool TryBreach()
	{
		if (PlanksPlaced > 0)
		{
			PlanksPlaced--;
			AudioManager.Sfx("window_rattle");
			return false;
		}

		return true;
	}

	public void TryBreakAll()
	{
		PlanksPlaced = 0;
		UpdateVisuals();
	}

	private void UpdateVisuals()
	{
		if (!_setupDone)
			return;

		if (LockVisual != null)
			LockVisual.Visible = false;

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
		_hud ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		if (_hud != null)
		{
			if (string.IsNullOrEmpty(text))
				_hud.ClearInteractionPrompt();
			else
				_hud.SetInteractionPrompt(text);
		}

		if (StatusLabel != null)
		{
			if (string.IsNullOrEmpty(text))
				StatusLabel.Visible = false;
			else
			{
				StatusLabel.Text = text;
				StatusLabel.Visible = true;
			}
		}
	}

	private void ShowMessage(string text, float duration = 1.6f)
	{
		SetStatus(text);
		_msgTimer = duration;
	}
}
