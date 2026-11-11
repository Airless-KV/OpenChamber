// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - 2.5D Hot Air Balloon Flower Shop flight state machine (Dawn arrival, Dusk departure)
//                   - Day/Night transition animation with vertical cubic easing and idle hover drift
//                   - Safe HUD shop closure on nightfall to release player controls
//                   - Basket interaction prompt and burner illumination management
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 11, 2026 | air : Created 2.5D HotAirBalloonShop with dawn arrival, dusk departure, and idle bobbing
// =========================================================================================
using Godot;
using System;

public partial class HotAirBalloonShop : InteractableShop
{
	[ExportGroup("Flight Parameters")]
	[Export] public float GroundedY = 1.72f;
	[Export] public float SkyY = 50.0f;
	[Export] public float FlightDuration = 5.5f;
	[Export] public float IdleBobDistance = 0.15f;
	[Export] public float IdleBobDuration = 2.4f;

	[ExportGroup("Visual & Collision References")]
	[Export] public Node3D VisualRoot;
	[Export] public CollisionShape3D BasketCollider;
	[Export] public OmniLight3D BurnerLight;

	public bool IsGrounded { get; private set; } = true;
	private Tween _flightTween;
	private Tween _bobTween;
	private Vector3 _homeGroundedPos;

	public override void _Ready()
	{
		base._Ready();

		_homeGroundedPos = GlobalPosition;
		_homeGroundedPos.Y = GroundedY;

		if (GameManagerNode != null)
		{
			GameManagerNode.PhaseChanged += OnGamePhaseChanged;

			// Determine initial flight state based on current phase
			bool isDay = GameManagerNode.CurrentPhase == GameManager.GamePhase.Day;
			if (isDay)
			{
				GlobalPosition = _homeGroundedPos;
				IsGrounded = true;
				StartIdleBobbing();
			}
			else
			{
				Vector3 skyPos = _homeGroundedPos;
				skyPos.Y = SkyY;
				GlobalPosition = skyPos;
				IsGrounded = false;
				if (BasketCollider != null) BasketCollider.Disabled = true;
			}
		}
		else
		{
			StartIdleBobbing();
		}
	}

	private void OnGamePhaseChanged(bool isDay, int currentDay)
	{
		if (isDay)
		{
			ArriveAtDawn();
		}
		else
		{
			DepartAtDusk();
		}
	}

	public void ArriveAtDawn()
	{
		_bobTween?.Kill();
		_flightTween?.Kill();

		if (BasketCollider != null) BasketCollider.Disabled = false;

		// Ensure starting high in the sky
		Vector3 startPos = _homeGroundedPos;
		startPos.Y = SkyY;
		GlobalPosition = startPos;

		if (BurnerLight != null) BurnerLight.Visible = true;
		AudioManager.Instance?.PlaySfx("door_thud", 0.85f);

		_flightTween = CreateTween();
		_flightTween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		_flightTween.TweenProperty(this, "global_position:y", _homeGroundedPos.Y, FlightDuration);
		_flightTween.TweenCallback(Callable.From(() =>
		{
			IsGrounded = true;
			StartIdleBobbing();
		}));
	}

	public void DepartAtDusk()
	{
		IsGrounded = false;
		_bobTween?.Kill();
		_flightTween?.Kill();

		// Safely close shop if player is currently in the menu
		if (HUDNode != null && HUDNode.IsShopOpen)
		{
			HUDNode.CloseShop();
		}

		if (BasketCollider != null) BasketCollider.Disabled = true;
		if (BurnerLight != null) BurnerLight.Visible = true;
		AudioManager.Instance?.PlaySfx("door_thud", 1.15f);

		_flightTween = CreateTween();
		_flightTween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
		_flightTween.TweenProperty(this, "global_position:y", SkyY, FlightDuration);
		_flightTween.TweenCallback(Callable.From(() =>
		{
			if (BurnerLight != null) BurnerLight.Visible = false;
		}));
	}

	private void StartIdleBobbing()
	{
		if (!IsGrounded) return;

		_bobTween?.Kill();
		_bobTween = CreateTween().SetLoops();
		_bobTween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		_bobTween.TweenProperty(this, "global_position:y", _homeGroundedPos.Y + IdleBobDistance, IdleBobDuration * 0.5f);
		_bobTween.TweenProperty(this, "global_position:y", _homeGroundedPos.Y - IdleBobDistance * 0.4f, IdleBobDuration * 0.5f);
	}

	public override string GetPrompt()
	{
		if (!IsGrounded) return "";
		return base.GetPrompt();
	}

	protected override void OnInteract(PlayerController player)
	{
		if (!IsGrounded) return;
		base.OnInteract(player);
	}
}
