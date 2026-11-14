// =========================================================================================
// Original Author : rbn (Robin Velasco) - Nov 06, 2026
// Responsibilities: - Bare opening barricade defense mechanics
//                   - Wooden plank placement [G] and removal [F] (3s hold)
//                   - Physical 3D plank consumption and ground drop refund
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
// - Nov 13, 2026 | air : Decommissioned Lawnmower defense; integrated physical 3D plank consumption & drops
// - Nov 14, 2026 | air : Added Rusted Crowbar instant barricade dismantle (0s delay) and enforced strict active hand plank holding
// - Nov 14, 2026 | air : Reduced plank placement duration to 1.5s (from 3.0s) and updated barricade prompts
// =========================================================================================
using Godot;

public partial class LockableOpening : Node3D
{
	[ExportGroup("Interaction Range")]
	[Export] public float InteractRange = 4.0f;
	[Export] public float LookDot = 0.7f;

	[ExportGroup("Barricade Timers & Sliders")]
	[Export(PropertyHint.Range, "0.5,10.0,0.1")] public float PlacePlankSeconds = 1.5f;
	[Export(PropertyHint.Range, "0.5,10.0,0.1")] public float RemovePlankSeconds = 3.0f;
	[Export] public int MaxPlanks = 3;
	[Export(PropertyHint.Enum, "North,East,South,West")] public int AttackDirection = 0;

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
		var player = GetTree().GetFirstNodeInGroup("Player") as PlayerController;
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
		bool isHoldingCrowbar = inv != null && inv.HasActiveItem(ItemType.Crowbar);
		bool isHoldingPlank = inv != null && inv.HasActiveItem(ItemType.Plank);

		// 1. Rusted Crowbar Instant Dismantle Mechanic
		if (isHoldingCrowbar)
		{
			bool isCrowbarClick = Input.IsActionJustPressed("lock_hold") ||
								  Input.IsActionJustPressed("place_barricade") ||
								  Input.IsMouseButtonPressed(MouseButton.Right) ||
								  Input.IsMouseButtonPressed(MouseButton.Left);

			if (PlanksPlaced > 0)
			{
				SetStatus($"[RUSTED CROWBAR] Click [LMB / RMB] to Instant Dismantle ({PlanksPlaced}/{MaxPlanks} Planks)");
				if (isCrowbarClick)
				{
					int count = PlanksPlaced;
					PlanksPlaced = 0;

					Node sceneRoot = GetTree().CurrentScene ?? (Node)this;
					Vector3 dropPos = GlobalPosition + new Vector3(0, 0.25f, 0);
					for (int i = 0; i < count; i++)
					{
						Vector3 offset = new Vector3((i - (count - 1) * 0.5f) * 0.45f, 0, 0.5f);
						WorldItem.Spawn(sceneRoot, dropPos + offset, ItemType.Plank);
					}

					if (_gm != null)
						_gm.RefundPlanks(count);

					AudioManager.Sfx("door_pound", 1.5f);
					AudioManager.Sfx("window_rattle", 2f);
					ShowMessage($"[✓] Crowbar instantly dismantled all {count} barricade planks!");
					UpdateVisuals();
					_held = 0f;
					_placeHeld = 0f;
					_toggled = true;
					_placeToggled = true;
				}
			}
			else
			{
				SetStatus("[RUSTED CROWBAR] Bare Opening - No planks to dismantle");
			}
			return;
		}

		// 2. Standard Plank Removal (3s hold Right Click)
		bool isRemovePressed = Input.IsActionPressed("lock_hold") || Input.IsMouseButtonPressed(MouseButton.Right);
		if (!isRemovePressed)
		{
			_held = 0f;
			_toggled = false;

			if (_placeHeld <= 0f)
			{
				if (PlanksPlaced > 0)
				{
					if (isHoldingPlank)
						SetStatus($"BARRICADED ({PlanksPlaced}/{MaxPlanks}) | Hold Left Click (1.5s): Place Plank | Hold Right Click (3s): Remove");
					else
						SetStatus($"BARRICADED ({PlanksPlaced}/{MaxPlanks}) | Hold Right Click (3s): Remove | (Hold Plank to Add)");
				}
				else
				{
					if (isHoldingPlank)
						SetStatus("[BARE OPENING] | Hold Left Click (1.5s) to Place Plank");
					else
						SetStatus("[BARE OPENING] | (Hold a Wooden Plank in hand to barricade)");
				}
			}
			return;
		}

		if (_toggled)
			return;

		if (PlanksPlaced == 0)
		{
			ShowMessage("Opening is bare - hold a Wooden Plank and hold Left Click (1.5s) to place!");
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
			// Spawn 3D WorldItem Planks on the floor
			Node sceneRoot = GetTree().CurrentScene ?? (Node)this;
			Vector3 dropPos = GlobalPosition + new Vector3(0, 0.25f, 0);
			for (int i = 0; i < PlanksPlaced; i++)
			{
				Vector3 offset = new Vector3((i - (PlanksPlaced - 1) * 0.5f) * 0.45f, 0, 0.5f);
				WorldItem.Spawn(sceneRoot, dropPos + offset, ItemType.Plank);
			}

			if (_gm != null)
				_gm.RefundPlanks(PlanksPlaced);

			PlanksPlaced = 0;
			AudioManager.Sfx("door_thud", -4f);
			ShowMessage("Planks removed - wooden planks dropped on the floor");
			UpdateVisuals();
		}
	}

	private void HandleBarricade(double delta)
	{
		var player = GetTree().GetFirstNodeInGroup("Player") as PlayerController;
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");

		// Crowbar already handled in HandleHold
		if (inv != null && inv.HasActiveItem(ItemType.Crowbar))
			return;

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

		// STRICT RULE: Must actively hold a Wooden Plank in hand!
		if (inv == null || !inv.HasActiveItem(ItemType.Plank))
		{
			ShowMessage("Cannot place! Actively hold a Wooden Plank in hand to barricade.");
			_placeHeld = 0f;
			return;
		}

		_placeHeld += (float)delta;
		int percent = Mathf.Min(100, (int)(_placeHeld / PlacePlankSeconds * 100));
		SetStatus($"Placing plank... {percent}%");

		if (_placeHeld < PlacePlankSeconds)
			return;

		_placeToggled = true;
		_placeHeld = 0f;

		if (inv.ConsumeActiveItem())
		{
			PlanksPlaced++;
			AudioManager.Sfx("door_thud", -4f);
			ShowMessage($"Plank placed ({PlanksPlaced}/{MaxPlanks})");
			UpdateVisuals();
		}
		else
		{
			ShowMessage("No planks! Carry a Wooden Plank to barricade.");
		}
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
