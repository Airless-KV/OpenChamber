// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - 8x8 chessboard farming grid coordinate math and spatial mapping
//                   - Drag-and-drop crop merge formula (Tier N + Tier N = Tier N + 1)
//                   - 5 flower tier materials (White/Yellow to Emerald Green) & scoring
//                   - Raycasting mouse hover detection and TileSelector box
//                   - Seed economy consumption & flower harvesting for coins
//                   - Staged growth timer system (Tier 1: 30s -> Tier 5: 3 min) and scaled profit
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined grid coordinate math, fallback mesh generation, and scoring queries for Alpha build
// - Oct 30, 2026 | air : Adapted grid hover raycast to first-person crosshair center targeting
// - Oct 30, 2026 | air : Implemented drag-and-drop flower merge formula (N+N->N+1) and 5 tier materials
// - Oct 31, 2026 | air : Consolidated tier attributes into FlowerTier struct, optimized procedural mesh instantiation
// - Oct 31, 2026 | air : Unified grid state storage into single Tile class and streamlined drag-and-drop execution
// - Oct 31, 2026 | air : Migrated procedural flower mesh construction to pure scene asset (res://scenes/flower.tscn)
// - Nov 06, 2026 | air : Added seed requirement check on planting and flower harvesting for coins on RMB
// - Nov 10, 2026 | air : Integrated 2.5D billboard pixel-art flower sprites and audio feedback
// - Nov 10, 2026 | air : Aligned grid to chessboard center, resolved floor clipping at Y=1.25, and added staged growth timer (30s to 3m) with scaled harvest profit
// - Nov 10, 2026 | air : Shortened farming raycast range to 7m and restricted indoor hover and planting
// - Nov 10, 2026 | air : Implemented closed bud seedling stage, dynamic bloom texture swap, and unharvestable closed state
// - Nov 10, 2026 | air : Restricted flower merging to fully bloomed/mature flowers only, blocking closed buds
// - Nov 13, 2026 | air : Harvested flowers spawn as physical 3D world entities; planting consumes held 3D seeds
// - Nov 14, 2026 | air : Removed soil tilling for direct planting and suppressed hover feedback when empty-handed
// - Nov 14, 2026 | air : Enforced strict active seed holding (HasActiveItem) for planting and removed abstract seed fallback
// =========================================================================================
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Node3d : Node3D
{
	public enum TileState { Empty, Planted }

	public readonly record struct FlowerTier(Color Color, int Points, float Scale);

	public static readonly FlowerTier[] Tiers =
	{
		new(new(0.96f, 0.95f, 0.86f), 5, 0.85f),  // Tier 1: Daisy White / Cream (5 pts)
		new(new(1.00f, 0.82f, 0.15f), 10, 1.05f), // Tier 2: Golden Sunburst Yellow (10 pts)
		new(new(0.98f, 0.45f, 0.15f), 15, 1.25f), // Tier 3: Sunset Coral / Orange (15 pts)
		new(new(0.88f, 0.12f, 0.32f), 20, 1.45f), // Tier 4: Royal Crimson Rose (20 pts)
		new(new(0.12f, 0.92f, 0.42f), 25, 1.65f)  // Tier 5: Mythic Emerald Bloom (25 pts)
	};

	public static readonly int[] TierPoints = { 5, 10, 15, 20, 25 };
	public static readonly float[] GrowthTimes = { 30.0f, 60.0f, 90.0f, 135.0f, 180.0f };
	public static readonly int[] HarvestCoins = { 3, 8, 20, 45, 100 };

	[ExportGroup("Grid Dimensions")]
	[Export] public int GridWidth = 8;
	[Export] public int GridHeight = 8;
	[Export] public float CellSize = 6.0f;
	[Export] public Vector3 GridOrigin = new(-25.6614f, 1.25f, -24.0f);
	[Export] public float FarmingRaycastRange = 7.0f;

	[ExportGroup("Scene References")]
	[Export] public Node3D TileSelector;
	[Export] public PackedScene CropPrefab;
	[Export] public HUDManager HUDNode;
	[Export] public GameManager GameManagerNode;
	[Export] public PlayerController PlayerNode;

	private sealed class Tile
	{
		public TileState State;
		public int Tier;
		public Node3D Visual;
		public float GrowthTimer;
		public float RequiredGrowthTime;
		public bool IsMature => GrowthTimer >= RequiredGrowthTime;
		public bool HasPlayedMatureTween;
	}

	private readonly Dictionary<Vector2I, Tile> _tiles = new();
	private Vector2I? _hoveredCell;
	private Vector2I? _heldCell;
	private ulong _pressTime;
	private bool _isDragging;
	private string _lastPrompt = "";

	public override void _Ready()
	{
		var board = GetNodeOrNull<Node3D>("../grassPlaceHolder");
		if (board != null)
		{
			GridOrigin = new Vector3(board.GlobalPosition.X - 24.0f, 1.25f, board.GlobalPosition.Z - 24.0f);
		}

		TileSelector ??= GetNodeOrNull<Node3D>("../TileSelector");
		if (TileSelector != null) TileSelector.Visible = false;
		HUDNode ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		GameManagerNode ??= GetTree().Root.FindChild("GameManager", true, false) as GameManager;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		UpdateGrowth(dt);
		UpdateHover();

		if (_heldCell.HasValue && GetTile(_heldCell.Value)?.Visual is { } held)
		{
			Vector3 target = GridToWorld(_hoveredCell ?? _heldCell.Value) + new Vector3(0, 1.8f, 0);
			held.GlobalPosition = held.GlobalPosition.Lerp(target, 20f * dt);
		}
	}

	private void UpdateGrowth(float dt)
	{
		foreach (var kvp in _tiles)
		{
			var tile = kvp.Value;
			if (tile.State != TileState.Planted || tile.Visual == null)
				continue;

			if (tile.GrowthTimer < tile.RequiredGrowthTime)
			{
				tile.GrowthTimer += dt;
				float progress = Mathf.Clamp(tile.GrowthTimer / tile.RequiredGrowthTime, 0f, 1f);
				float tierScale = Tiers[Mathf.Clamp(tile.Tier - 1, 0, 4)].Scale;
				float currentScale = Mathf.Lerp(tierScale * 0.7f, tierScale, progress);
				tile.Visual.Scale = Vector3.One * currentScale;

				if (tile.IsMature && !tile.HasPlayedMatureTween)
				{
					tile.HasPlayedMatureTween = true;

					// Bloom! Swap visual texture from closed bud to bloomed tier texture
					if (tile.Visual?.GetNodeOrNull<Sprite3D>("FlowerSprite") is { } sprite)
					{
						var tex = GetFlowerTexture(tile.Tier);
						if (tex != null)
							sprite.Texture = tex;

						if (tile.Tier == 5)
							sprite.Modulate = new Color(1.18f, 1.18f, 1.12f, 1.0f);
						else
							sprite.Modulate = Colors.White;
					}

					PlayMatureBounce(tile.Visual, tierScale);
					AudioManager.Sfx("flower_merge", -2f, 0.1f);
				}
			}
		}
	}

	private void PlayMatureBounce(Node3D visual, float targetScale)
	{
		if (visual == null) return;
		Tween tw = CreateTween();
		tw.TweenProperty(visual, "scale", Vector3.One * (targetScale * 1.25f), 0.15f);
		tw.TweenProperty(visual, "scale", Vector3.One * targetScale, 0.2f)
			.SetTrans(Tween.TransitionType.Bounce)
			.SetEase(Tween.EaseType.Out);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } btn)
		{
			if (btn.Pressed) OnLeftClickPressed();
			else OnLeftClickReleased();
		}
		else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
		{
			if (_heldCell.HasValue) CancelHold();
			else if (_hoveredCell.HasValue) HarvestCrop(_hoveredCell.Value);
		}
	}

	private Tile GetTile(Vector2I c) => _tiles.GetValueOrDefault(c);

	private void SetPrompt(string prompt)
	{
		if (_lastPrompt == prompt) return;
		_lastPrompt = prompt;
		HUDNode?.SetInteractionPrompt(prompt);
	}

	private void ClearPrompt()
	{
		if (string.IsNullOrEmpty(_lastPrompt)) return;
		_lastPrompt = "";
		HUDNode?.ClearInteractionPrompt();
	}

	private void UpdateHover()
	{
		PlayerNode ??= GetTree().GetFirstNodeInGroup("Player") as PlayerController;
		if (PlayerNode != null && PlayerNode.IsIndoors)
		{
			_hoveredCell = null;
			if (TileSelector != null) TileSelector.Visible = false;
			if (_heldCell.HasValue) SetPrompt("[RMB] Cancel Drag");
			else ClearPrompt();
			return;
		}

		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null) return;

		var hit = GetWorld3D().DirectSpaceState.IntersectRay(new PhysicsRayQueryParameters3D
		{
			From = cam.GlobalPosition,
			To = cam.GlobalPosition - cam.GlobalTransform.Basis.Z * FarmingRaycastRange
		});

		if (hit.Count > 0 && WorldToGrid((Vector3)hit["position"]) is var cell && IsInsideGrid(cell))
		{
			_hoveredCell = cell;
			var tile = GetTile(cell);
			bool isPlanted = tile?.State == TileState.Planted;
			bool isDragging = _heldCell.HasValue;
			var inv = PlayerNode?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
			bool hasSeed = inv != null && inv.HasActiveItem(ItemType.Seed);

			// Empty-handed over empty soil: suppress hover selector highlight and prompt
			bool shouldShowSelector = isPlanted || isDragging || hasSeed;
			if (TileSelector != null)
			{
				TileSelector.Visible = shouldShowSelector;
				if (shouldShowSelector)
					TileSelector.GlobalPosition = GridToWorld(cell) + new Vector3(0, 0.05f, 0);
			}

			UpdatePrompt(cell);
			return;
		}

		_hoveredCell = null;
		if (TileSelector != null) TileSelector.Visible = false;
		if (_heldCell.HasValue) SetPrompt("[RMB] Cancel Drag");
		else ClearPrompt();
	}

	private void UpdatePrompt(Vector2I cell)
	{
		var tile = GetTile(cell);
		if (_heldCell.HasValue)
		{
			var heldTile = GetTile(_heldCell.Value);
			int heldTier = heldTile?.Tier ?? 1;
			if (cell == _heldCell.Value) SetPrompt($"[LMB] Put down Tier {heldTier} Flower | [RMB] Cancel");
			else if (tile?.State == TileState.Planted)
			{
				if (tile.Tier == heldTier && heldTier < 5)
				{
					bool heldMature = heldTile?.IsMature ?? false;
					if (heldMature && tile.IsMature)
						SetPrompt($"[★ MERGE!] Tier {heldTier} + Tier {heldTier} -> Tier {heldTier + 1}! (+{TierPoints[heldTier]} pts)");
					else
						SetPrompt("[!] Both flowers must be fully bloomed to merge!");
				}
				else
					SetPrompt($"[LMB] Swap with Tier {tile.Tier} Flower | [RMB] Cancel");
			}
			else SetPrompt($"[LMB] Move Tier {heldTier} Flower here | [RMB] Cancel");
			return;
		}

		var state = tile?.State ?? TileState.Empty;
		if (state == TileState.Planted)
		{
			int tier = Mathf.Clamp(tile.Tier, 1, 5);

			if (tile.IsMature)
			{
				SetPrompt($"[✓ BLOOMED & READY!] Tier {tier} Flower | [RMB] Harvest to 3D Item | [LMB] Drag & Merge");
			}
			else
			{
				float remaining = Mathf.Max(0f, tile.RequiredGrowthTime - tile.GrowthTimer);
				SetPrompt($"[Growing closed bud... {Mathf.CeilToInt(remaining)}s] Tier {tier} Flower | [LMB] Move | Needs to bloom before merging & harvesting");
			}
		}
		else
		{
			// Empty soil tile
			var inv = PlayerNode?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
			bool hasSeed = inv != null && inv.HasActiveItem(ItemType.Seed);
			if (hasSeed)
			{
				SetPrompt("[LMB] Plant Tier 1 Flower (Seed ready)");
			}
			else
			{
				// Empty-handed: no hover prompt feedback!
				ClearPrompt();
			}
		}
	}

	private void OnLeftClickPressed()
	{
		if (!_hoveredCell.HasValue) return;
		Vector2I cell = _hoveredCell.Value;

		if (_heldCell.HasValue) { ExecuteDrop(cell); return; }

		var tile = GetTile(cell);
		if (tile?.State == TileState.Planted)
		{
			_heldCell = cell;
			_pressTime = Time.GetTicksMsec();
			_isDragging = true;
		}
		else
		{
			// Direct planting on empty tile: strictly require actively holding a seed in hand
			var inv = PlayerNode?.GetNodeOrNull<CarriedInventory>("CarriedInventory");
			if (inv != null && inv.HasActiveItem(ItemType.Seed))
			{
				inv.ConsumeActiveItem();
				PlantCropAt(cell, 1);
				AudioManager.Sfx("flower_plant");
				SetPrompt("Planted Tier 1 Flower! Closed bud growing...");
			}
		}
	}

	private void OnLeftClickReleased()
	{
		if (!_heldCell.HasValue || !_isDragging) return;
		_isDragging = false;
		if (Time.GetTicksMsec() - _pressTime > 220 || (_hoveredCell.HasValue && _hoveredCell.Value != _heldCell.Value))
		{
			if (_hoveredCell.HasValue) ExecuteDrop(_hoveredCell.Value);
			else CancelHold();
		}
	}

	private void ExecuteDrop(Vector2I target)
	{
		if (!_heldCell.HasValue) return;
		Vector2I from = _heldCell.Value;
		if (target == from) { ResetVisual(from); _heldCell = null; return; }

		var fromTile = GetTile(from);
		var targetTile = GetTile(target);

		if (targetTile?.State == TileState.Planted && fromTile != null)
		{
			if (fromTile.Tier == targetTile.Tier && fromTile.Tier < 5)
			{
				if (!fromTile.IsMature || !targetTile.IsMature)
				{
					SetPrompt("[!] Only fully bloomed flowers can be merged! Wait for both to grow.");
					ResetVisual(from);
					_heldCell = null;
					return;
				}

				int newTier = fromTile.Tier + 1;
				fromTile.Visual?.QueueFree();
				_tiles.Remove(from);

				targetTile.Visual?.QueueFree();
				var flower = SpawnFlower(newTier, isBloomed: false);
				AddChild(flower);
				flower.GlobalPosition = GridToWorld(target);

				float reqTime = GrowthTimes[newTier - 1];
				_tiles[target] = new Tile
				{
					State = TileState.Planted,
					Tier = newTier,
					Visual = flower,
					GrowthTimer = 0f,
					RequiredGrowthTime = reqTime,
					HasPlayedMatureTween = false
				};

				float s = Tiers[newTier - 1].Scale;
				flower.Scale = Vector3.One * (s * 0.4f);
				Tween tw = CreateTween();
				tw.TweenProperty(flower, "scale", Vector3.One * (s * 1.35f), 0.12f);
				tw.TweenProperty(flower, "scale", Vector3.One * (s * 0.7f), 0.15f);

				AudioManager.Sfx("flower_merge");
				SetPrompt($"[★ MERGED!] Level {newTier} Flower created! Growing closed bud... ({Mathf.CeilToInt(reqTime)}s until bloom)");
				SaveManager.RecordMerge();
			}
			else
			{
				_tiles[from] = targetTile;
				_tiles[target] = fromTile;
				if (fromTile.Visual != null) fromTile.Visual.GlobalPosition = GridToWorld(target);
				if (targetTile.Visual != null) targetTile.Visual.GlobalPosition = GridToWorld(from);
				SetPrompt("Swapped flower positions.");
			}
		}
		else if (fromTile != null)
		{
			_tiles[target] = fromTile;
			_tiles.Remove(from);
			if (fromTile.Visual != null) fromTile.Visual.GlobalPosition = GridToWorld(target);
			SetPrompt($"Moved Tier {fromTile.Tier} flower.");
		}
		_heldCell = null;
	}

	private void CancelHold()
	{
		if (!_heldCell.HasValue) return;
		ResetVisual(_heldCell.Value);
		_heldCell = null;
		ClearPrompt();
	}

	private void ResetVisual(Vector2I cell)
	{
		if (GetTile(cell)?.Visual is { } vis) vis.GlobalPosition = GridToWorld(cell);
	}

	private void PlantCropAt(Vector2I cell, int level = 1)
	{
		level = Mathf.Clamp(level, 1, 5);
		Node3D crop = SpawnFlower(level, isBloomed: false);
		AddChild(crop);
		crop.GlobalPosition = GridToWorld(cell);

		float reqTime = GrowthTimes[level - 1];
		_tiles[cell] = new Tile
		{
			State = TileState.Planted,
			Tier = level,
			Visual = crop,
			GrowthTimer = 0f,
			RequiredGrowthTime = reqTime,
			HasPlayedMatureTween = false
		};

		SetPrompt($"Planted Tier {level} Flower! Growing closed bud... ({Mathf.CeilToInt(reqTime)}s until bloom)");
	}

	private static readonly string[] FlowerTexturePaths =
	{
		"res://assets/textures/flowers/flower_lvl1.png",
		"res://assets/textures/flowers/flower_lvl2.png",
		"res://assets/textures/flowers/flower_lvl3.png",
		"res://assets/textures/flowers/flower_lvl4.png",
		"res://assets/textures/flowers/flower_lvl5.png"
	};

	private static readonly Texture2D[] _cachedFlowerTextures = new Texture2D[5];
	private static Texture2D _cachedClosedTexture;

	public static Texture2D GetClosedFlowerTexture()
	{
		if (_cachedClosedTexture == null)
		{
			const string path = "res://assets/textures/flowers/flower_closed.png";
			if (ResourceLoader.Exists(path))
				_cachedClosedTexture = GD.Load<Texture2D>(path);
		}
		return _cachedClosedTexture;
	}

	public static Texture2D GetFlowerTexture(int level)
	{
		int idx = Mathf.Clamp(level - 1, 0, FlowerTexturePaths.Length - 1);
		if (_cachedFlowerTextures[idx] == null)
		{
			string path = FlowerTexturePaths[idx];
			if (ResourceLoader.Exists(path))
				_cachedFlowerTextures[idx] = GD.Load<Texture2D>(path);
		}
		return _cachedFlowerTextures[idx];
	}

	private Node3D SpawnFlower(int level, bool isBloomed = false)
	{
		level = Mathf.Clamp(level, 1, 5);
		var tier = Tiers[level - 1];
		CropPrefab ??= GD.Load<PackedScene>("res://scenes/flower.tscn");
		Node3D flower = CropPrefab?.Instantiate<Node3D>() ?? new Node3D();
		flower.Name = $"Flower_T{level}";
		flower.Scale = Vector3.One * (tier.Scale * (isBloomed ? 1.0f : 0.7f));

		var sprite = flower.GetNodeOrNull<Sprite3D>("FlowerSprite") ?? flower.FindChild("FlowerSprite", true, false) as Sprite3D;
		if (sprite != null)
		{
			var tex = isBloomed ? GetFlowerTexture(level) : GetClosedFlowerTexture();
			if (tex != null)
				sprite.Texture = tex;

			if (isBloomed && level == 5)
				sprite.Modulate = new Color(1.18f, 1.18f, 1.12f, 1.0f);
			else
				sprite.Modulate = Colors.White;
		}

		var blossomMat = new StandardMaterial3D
		{
			AlbedoColor = tier.Color,
			Roughness = 0.35f,
			EmissionEnabled = level == 5,
			Emission = new Color(0.08f, 0.55f, 0.25f),
			EmissionEnergyMultiplier = level == 5 ? 1.5f : 0.0f
		};

		if (flower.GetNodeOrNull<MeshInstance3D>("Blossom") is { } blossom)
			blossom.MaterialOverride = blossomMat;
		if (flower.GetNodeOrNull<MeshInstance3D>("Petals") is { } petals)
			petals.MaterialOverride = blossomMat;

		flower.SetMeta("Level", level);
		return flower;
	}

	private void HarvestCrop(Vector2I cell)
	{
		var tile = GetTile(cell);
		if (tile?.State == TileState.Planted)
		{
			int tier = Mathf.Clamp(tile.Tier, 1, 5);
			if (!tile.IsMature)
			{
				float remaining = Mathf.Max(0f, tile.RequiredGrowthTime - tile.GrowthTimer);
				SetPrompt($"[!] Flower is still a closed bud! ({Mathf.CeilToInt(remaining)}s left before bloom)");
				return;
			}

			// Free crop visual and reset tile to empty
			tile.Visual?.QueueFree();
			_tiles.Remove(cell);

			// Spawn physical 3D WorldItem on the chessboard tile
			Vector3 dropPos = GridToWorld(cell) + new Vector3(0, 0.4f, 0);
			Node sceneRoot = GetTree().CurrentScene ?? (Node)this;
			WorldItem.Spawn(sceneRoot, dropPos, ItemType.Flower, tier);

			AudioManager.Sfx("flower_merge", -2f);
			SetPrompt($"[✓] Harvested Tier {tier} Flower! [Space] to Grab & Carry");
		}
	}

	public Vector2I WorldToGrid(Vector3 worldPos) => new(
		Mathf.FloorToInt((worldPos.X - GridOrigin.X) / CellSize),
		Mathf.FloorToInt((worldPos.Z - GridOrigin.Z) / CellSize)
	);

	public Vector3 GridToWorld(Vector2I cell) => new(
		GridOrigin.X + (cell.X + 0.5f) * CellSize,
		GridOrigin.Y,
		GridOrigin.Z + (cell.Y + 0.5f) * CellSize
	);

	public bool IsInsideGrid(Vector2I cell) => cell.X >= 0 && cell.X < GridWidth && cell.Y >= 0 && cell.Y < GridHeight;

	public int GetPlantedCropCount() => _tiles.Values.Count(t => t.State == TileState.Planted);

	public Dictionary<int, int> GetFlowerTierCounts()
	{
		var counts = new Dictionary<int, int> { { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 } };
		foreach (var t in _tiles.Values)
			if (t.State == TileState.Planted && t.Tier is >= 1 and <= 5)
				counts[t.Tier]++;
		return counts;
	}

	public Vector2I? GetHighestTierPlantedCell()
	{
		return _tiles
			.Where(kvp => kvp.Value.State == TileState.Planted && kvp.Value.Tier >= 1)
			.OrderByDescending(kvp => kvp.Value.Tier)
			.Select(kvp => (Vector2I?)kvp.Key)
			.FirstOrDefault();
	}

	public int GetCropTier(Vector2I cell)
	{
		var t = GetTile(cell);
		return (t?.State == TileState.Planted) ? t.Tier : 0;
	}

	public bool DestroyCropAt(Vector2I cell)
	{
		var tile = GetTile(cell);
		if (tile?.State != TileState.Planted) return false;

		tile.Visual?.QueueFree();
		_tiles[cell] = new Tile { State = TileState.Empty };
		AudioManager.Sfx("foliage_rustle");
		return true;
	}
}
