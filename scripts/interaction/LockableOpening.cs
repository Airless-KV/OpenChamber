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
// - Nov 14, 2026 | air : Triggered CameraPivot house attack trauma jolt when tearing barricade planks
// - Nov 15, 2026 | air : Decommissioned active barricade mechanics per gameplay simplification
// - Nov 16, 2026 | air : Removed obsolete PlankVisuals export to resolve Godot serialization bug
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

		// Barricade mechanics decommissioned - clear any status text
		SetStatus(null);
	}

	// Tears a single plank sequentially during Ani-Ani's assault
	public bool TearSinglePlank() => false;

	public bool TryBreach() => true;

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
