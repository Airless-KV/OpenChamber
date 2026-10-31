// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - 8x8 chessboard farming grid coordinate math and spatial mapping
//                   - Drag-and-drop crop merge formula (Tier N + Tier N = Tier N + 1)
//                   - 5 flower tier materials (White/Yellow to Emerald Green) & scoring
//                   - Raycasting mouse hover detection and TileSelector box
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Streamlined grid coordinate math, fallback mesh generation, and scoring queries for Alpha build
// - Oct 30, 2026 | air : Adapted grid hover raycast to first-person crosshair center targeting
// - Oct 30, 2026 | air : Implemented drag-and-drop flower merge formula (N+N->N+1) and 5 tier materials
// - Oct 31, 2026 | air : Consolidated tier attributes into FlowerTier struct, optimized procedural mesh instantiation
// - Oct 31, 2026 | air : Unified grid state storage into single Tile class and streamlined drag-and-drop execution
// - Oct 31, 2026 | air : Migrated procedural flower mesh construction to pure scene asset (res://scenes/flower.tscn)
// =========================================================================================
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Node3d : Node3D
{
	public enum TileState { Empty, Tilled, Planted }

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

	[ExportGroup("Grid Dimensions")]
	[Export] public int GridWidth = 8;
	[Export] public int GridHeight = 8;
	[Export] public float CellSize = 6.0f;
	[Export] public Vector3 GridOrigin = new(-23.97f, 0.0f, -23.97f);

	[ExportGroup("Scene References")]
	[Export] public Node3D TileSelector;
	[Export] public PackedScene CropPrefab;
	[Export] public HUDManager HUDNode;

	private sealed class Tile
	{
		public TileState State;
		public int Tier;
		public Node3D Visual;
	}

	private readonly Dictionary<Vector2I, Tile> _tiles = new();
	private Vector2I? _hoveredCell;
	private Vector2I? _heldCell;
	private ulong _pressTime;
	private bool _isDragging;
	private string _lastPrompt = "";

	public override void _Ready()
	{
		TileSelector ??= GetNodeOrNull<Node3D>("../TileSelector");
		if (TileSelector != null) TileSelector.Visible = false;
		HUDNode ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;
	}

	public override void _Process(double delta)
	{
		UpdateHover();
		if (_heldCell.HasValue && GetTile(_heldCell.Value)?.Visual is { } held)
		{
			Vector3 target = GridToWorld(_hoveredCell ?? _heldCell.Value) + new Vector3(0, 1.8f, 0);
			held.GlobalPosition = held.GlobalPosition.Lerp(target, 20f * (float)delta);
		}
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
			else if (_hoveredCell.HasValue) RemoveCrop(_hoveredCell.Value);
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
		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null) return;

		var hit = GetWorld3D().DirectSpaceState.IntersectRay(new PhysicsRayQueryParameters3D
		{
			From = cam.GlobalPosition,
			To = cam.GlobalPosition - cam.GlobalTransform.Basis.Z * 50f
		});

		if (hit.Count > 0 && WorldToGrid((Vector3)hit["position"]) is var cell && IsInsideGrid(cell))
		{
			_hoveredCell = cell;
			if (TileSelector != null)
			{
				TileSelector.Visible = true;
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
			int heldTier = GetTile(_heldCell.Value)?.Tier ?? 1;
			if (cell == _heldCell.Value) SetPrompt($"[LMB] Put down Tier {heldTier} Flower | [RMB] Cancel");
			else if (tile?.State == TileState.Planted)
			{
				if (tile.Tier == heldTier && heldTier < 5)
					SetPrompt($"[★ MERGE!] Tier {heldTier} + Tier {heldTier} -> Tier {heldTier + 1}! (+{TierPoints[heldTier]} pts)");
				else
					SetPrompt($"[LMB] Swap with Tier {tile.Tier} Flower | [RMB] Cancel");
			}
			else SetPrompt($"[LMB] Move Tier {heldTier} Flower here | [RMB] Cancel");
			return;
		}

		var state = tile?.State ?? TileState.Empty;
		if (state == TileState.Empty) SetPrompt("[LMB] Till Soil");
		else if (state == TileState.Tilled) SetPrompt("[LMB] Plant Tier 1 Flower (Daisy)");
		else SetPrompt($"[LMB] Grab Tier {tile.Tier} Flower ({TierPoints[tile.Tier - 1]} pts) to Drag & Merge | [RMB] Remove");
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
		else if (tile == null || tile.State == TileState.Empty)
		{
			_tiles[cell] = new Tile { State = TileState.Tilled };
			SetPrompt("[LMB] Plant Tier 1 Flower (Daisy)");
		}
		else if (tile.State == TileState.Tilled)
		{
			PlantCropAt(cell, 1);
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
				int newTier = fromTile.Tier + 1;
				fromTile.Visual?.QueueFree();
				_tiles[from] = new Tile { State = TileState.Tilled };

				targetTile.Visual?.QueueFree();
				var flower = SpawnFlower(newTier);
				AddChild(flower);
				flower.GlobalPosition = GridToWorld(target);
				_tiles[target] = new Tile { State = TileState.Planted, Tier = newTier, Visual = flower };

				float s = Tiers[newTier - 1].Scale;
				flower.Scale = Vector3.One * (s * 0.4f);
				Tween tw = CreateTween();
				tw.TweenProperty(flower, "scale", Vector3.One * (s * 1.35f), 0.12f);
				tw.TweenProperty(flower, "scale", Vector3.One * s, 0.15f);

				SetPrompt($"[★ MERGED!] Level {newTier} Flower created! (+{TierPoints[newTier - 1]} pts)");
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
			_tiles[from] = new Tile { State = TileState.Tilled };
			if (fromTile.Visual != null) fromTile.Visual.GlobalPosition = GridToWorld(target);
			SetPrompt($"Moved Tier {fromTile.Tier} flower to {target}.");
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
		Node3D crop = SpawnFlower(level);
		AddChild(crop);
		crop.GlobalPosition = GridToWorld(cell);
		_tiles[cell] = new Tile { State = TileState.Planted, Tier = level, Visual = crop };
		SetPrompt($"Planted Tier {level} Flower! (+{TierPoints[level - 1]} pts)");
	}

	private Node3D SpawnFlower(int level)
	{
		level = Mathf.Clamp(level, 1, 5);
		var tier = Tiers[level - 1];
		CropPrefab ??= GD.Load<PackedScene>("res://scenes/flower.tscn");
		Node3D flower = CropPrefab?.Instantiate<Node3D>() ?? new Node3D();
		flower.Name = $"Flower_T{level}";
		flower.Scale = Vector3.One * tier.Scale;

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

	private void RemoveCrop(Vector2I cell)
	{
		if (_tiles.Remove(cell, out var tile))
		{
			tile.Visual?.QueueFree();
			SetPrompt("Removed crop.");
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
}
