// =========================================================================================
// Original Author : air (Airless-KV) - Nov 2026
// Responsibilities: - 2.5D Hot Air Balloon Flower Shop flight state machine (Dawn arrival, Dusk departure)
//                   - Day/Night transition animation with diagonal flight towards mountain opening
//                   - Random outer chessboard perimeter tile touchdown at dawn
//                   - Safe HUD shop closure 30s before nightfall to release player controls
//                   - Basket interaction prompt and burner illumination management
// -----------------------------------------------------------------------------------------
// Change Log:
// - Nov 11, 2026 | air : Created 2.5D HotAirBalloonShop with dawn arrival, dusk departure, and idle bobbing
// - Nov 13, 2026 | air : Enlarged balloon, implemented diagonal flight towards mountain opening,
//                        random landing on outer chessboard tiles, and 30s pre-night departure
// - Nov 13, 2026 | air : Applied Option 2: Extended inbound flight to 40s and outbound flight to 30s
//                        with smooth Sine easing for majestic low-speed mountain drifting
// - Nov 13, 2026 | air : Added physical 3D flower selling interface with dynamic prompt and coin awards
// =========================================================================================
using Godot;
using System;
using System.Collections.Generic;

public partial class HotAirBalloonShop : InteractableShop
{
	[ExportGroup("Flight Parameters")]
	[Export] public float GroundedY = 1.25f;
	[Export] public Vector3 MountainOpeningSkyTarget = new(-55.0f, 65.0f, -210.0f);
	[Export] public float InboundFlightDuration = 40.0f;
	[Export] public float OutboundFlightDuration = 30.0f;
	[Export] public float DepartureLeadTime = 30.0f;
	[Export] public float IdleBobDistance = 0.20f;
	[Export] public float IdleBobDuration = 2.8f;

	[ExportGroup("Visual & Collision References")]
	[Export] public Node3D VisualRoot;
	[Export] public CollisionShape3D BasketCollider;
	[Export] public OmniLight3D BurnerLight;

	public bool IsGrounded { get; private set; } = false;
	private bool _isDeparting = false;
	private Tween _flightTween;
	private Tween _bobTween;
	private Vector3 _homeGroundedPos;
	private readonly System.Random _rng = new();

	// 28 perimeter cells along the outer border of the 8x8 chessboard
	private static readonly Vector2I[] OuterTiles;

	static HotAirBalloonShop()
	{
		var list = new List<Vector2I>();
		for (int x = 0; x < 8; x++) list.Add(new Vector2I(x, 0)); // North edge
		for (int x = 0; x < 8; x++) list.Add(new Vector2I(x, 7)); // South edge
		for (int z = 1; z < 7; z++) list.Add(new Vector2I(0, z)); // West edge
		for (int z = 1; z < 7; z++) list.Add(new Vector2I(7, z)); // East edge
		OuterTiles = list.ToArray();
	}

	public override void _Ready()
	{
		base._Ready();

		if (GameManagerNode != null)
		{
			GameManagerNode.PhaseChanged += OnGamePhaseChanged;
			GameManagerNode.TimeUpdated += OnTimeUpdated;

			// Determine initial flight state based on current phase
			bool isDay = GameManagerNode.CurrentPhase == GameManager.GamePhase.Day;
			if (isDay)
			{
				ArriveAtDawn();
			}
			else
			{
				GlobalPosition = MountainOpeningSkyTarget;
				IsGrounded = false;
				if (BasketCollider != null) BasketCollider.Disabled = true;
				if (BurnerLight != null) BurnerLight.Visible = false;
			}
		}
		else
		{
			_homeGroundedPos = PickRandomOuterTilePosition();
			GlobalPosition = _homeGroundedPos;
			IsGrounded = true;
			StartIdleBobbing();
		}
	}

	public Vector3 PickRandomOuterTilePosition()
	{
		int idx = _rng.Next(OuterTiles.Length);
		Vector2I cell = OuterTiles[idx];

		Node3D gridNode = GetParent()?.GetNodeOrNull<Node3D>("GridManager") ??
		                  GetTree()?.Root.FindChild("GridManager", true, false) as Node3D;

		if (gridNode != null && gridNode.HasMethod("GridToWorld"))
		{
			Variant res = gridNode.Call("GridToWorld", cell);
			if (res.VariantType == Variant.Type.Vector3)
			{
				Vector3 world = res.AsVector3();
				world.Y = GroundedY;
				return world;
			}
		}

		// Fallback: 8x8 Chessboard centered around (-1.66, 0), CellSize 6.0m
		float wx = -25.66f + (cell.X + 0.5f) * 6.0f;
		float wz = -24.0f + (cell.Y + 0.5f) * 6.0f;
		return new Vector3(wx, GroundedY, wz);
	}

	private void OnGamePhaseChanged(bool isDay, int currentDay)
	{
		if (isDay)
		{
			ArriveAtDawn();
		}
		else
		{
			if (IsGrounded || !_isDeparting)
			{
				DepartAtDusk();
			}
		}
	}

	private void OnTimeUpdated(float timeRemaining)
	{
		if (GameManagerNode == null) return;
		if (GameManagerNode.CurrentPhase != GameManager.GamePhase.Day) return;
		if (!IsGrounded || _isDeparting) return;

		// Fly away 30 seconds before nightfall
		if (timeRemaining <= DepartureLeadTime)
		{
			DepartAtDusk();
		}
	}

	public void ArriveAtDawn()
	{
		_isDeparting = false;
		IsGrounded = false;
		_bobTween?.Kill();
		_flightTween?.Kill();

		if (BasketCollider != null) BasketCollider.Disabled = true;

		// Select a fresh random perimeter tile for today's landing
		_homeGroundedPos = PickRandomOuterTilePosition();

		// Start high above the mountain valley opening
		GlobalPosition = MountainOpeningSkyTarget;

		if (BurnerLight != null)
		{
			BurnerLight.Visible = true;
			BurnerLight.LightEnergy = 2.4f;
		}

		AudioManager.Instance?.PlaySfx("door_thud", 0.9f);

		GD.Print($"[HotAirBalloonShop] Inbound 40s slow flight initiated from mountain opening to outer tile {_homeGroundedPos}.");

		_flightTween = CreateTween();
		_flightTween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		_flightTween.TweenProperty(this, "global_position", _homeGroundedPos, InboundFlightDuration);
		_flightTween.TweenCallback(Callable.From(() =>
		{
			IsGrounded = true;
			if (BasketCollider != null) BasketCollider.Disabled = false;
			StartIdleBobbing();
			AudioManager.Instance?.PlaySfx("foliage_rustle", 1.0f);
			GD.Print($"[HotAirBalloonShop] Touchdown on outer tile {_homeGroundedPos}. Shop is open!");
		}));
	}

	public void DepartAtDusk()
	{
		if (_isDeparting) return;
		_isDeparting = true;
		IsGrounded = false;
		_bobTween?.Kill();
		_flightTween?.Kill();

		// Safely close shop if player is currently browsing
		if (HUDNode != null && HUDNode.IsShopOpen)
		{
			HUDNode.CloseShop();
		}

		if (BasketCollider != null) BasketCollider.Disabled = true;
		if (BurnerLight != null)
		{
			BurnerLight.Visible = true;
			BurnerLight.LightEnergy = 3.2f; // Flare burner on lift-off
		}

		AudioManager.Instance?.PlaySfx("door_thud", 1.15f);

		GD.Print($"[HotAirBalloonShop] 30s pre-night departure: Ascending diagonally towards mountain opening over 30s.");

		_flightTween = CreateTween();
		_flightTween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		_flightTween.TweenProperty(this, "global_position", MountainOpeningSkyTarget, OutboundFlightDuration);
		_flightTween.TweenCallback(Callable.From(() =>
		{
			_isDeparting = false;
			if (BurnerLight != null) BurnerLight.Visible = false;
			GD.Print($"[HotAirBalloonShop] Outbound flight completed into mountain pass.");
		}));
	}

	private void StartIdleBobbing()
	{
		if (!IsGrounded || _isDeparting) return;

		_bobTween?.Kill();
		_bobTween = CreateTween().SetLoops();
		_bobTween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		_bobTween.TweenProperty(this, "global_position:y", _homeGroundedPos.Y + IdleBobDistance, IdleBobDuration * 0.5f);
		_bobTween.TweenProperty(this, "global_position:y", _homeGroundedPos.Y - IdleBobDistance * 0.35f, IdleBobDuration * 0.5f);
	}

	public override string GetPrompt()
	{
		if (!IsGrounded || _isDeparting) return "";

		var player = GetTree()?.GetFirstNodeInGroup("Player") as PlayerController;
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
		if (inv != null && inv.HasActiveItem(ItemType.Flower))
		{
			var slot = inv.Slots[inv.ActiveSlot];
			int sellVal = WorldItem.FlowerPrices[Mathf.Clamp(slot.FlowerTier - 1, 0, 4)];
			string flowerName = WorldItem.FlowerNames[Mathf.Clamp(slot.FlowerTier - 1, 0, 4)];
			return $"[Space / RMB] Sell {flowerName} (+{sellVal} Coins) | [LMB] Open Shop";
		}

		return base.GetPrompt();
	}

	protected override void OnInteract(PlayerController player)
	{
		if (!IsGrounded || _isDeparting) return;
		base.OnInteract(player);
	}

	protected override void OnInteractSecondary(PlayerController player)
	{
		if (!IsGrounded || _isDeparting) return;
		var inv = player?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
		if (inv != null && inv.HasActiveItem(ItemType.Flower))
		{
			inv.SellActiveFlower();
		}
	}
}
