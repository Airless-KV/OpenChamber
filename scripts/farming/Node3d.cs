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
// =========================================================================================
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Node3d : Node3D
{
	public enum TileState { Empty, Tilled, Planted }

	// 5 Flower Tiers: Colors, Points, and Model Scales
	public static readonly Color[] TierColors =
	{
		new(0.96f, 0.95f, 0.86f), // Tier 1: Daisy White / Cream (5 pts)
		new(1.00f, 0.82f, 0.15f), // Tier 2: Golden Sunburst Yellow (10 pts)
		new(0.98f, 0.45f, 0.15f), // Tier 3: Sunset Coral / Orange (15 pts)
		new(0.88f, 0.12f, 0.32f), // Tier 4: Royal Crimson Rose (20 pts)
		new(0.12f, 0.92f, 0.42f)  // Tier 5: Mythic Emerald Bloom (25 pts)
	};

	public static readonly int[] TierPoints = { 5, 10, 15, 20, 25 };
	public static readonly float[] TierScales = { 0.85f, 1.05f, 1.25f, 1.45f, 1.65f };

	[ExportGroup("Grid Dimensions")]
	[Export] public int GridWidth = 8;
	[Export] public int GridHeight = 8;
	[Export] public float CellSize = 6.0f;
	[Export] public Vector3 GridOrigin = new(-23.97f, 0.0f, -23.97f);

	[ExportGroup("Scene References")]
	[Export] public Node3D TileSelector;
	[Export] public PackedScene CropPrefab;
	[Export] public HUDManager HUDNode;

	private readonly Dictionary<Vector2I, TileState> _gridStates = new();
	private readonly Dictionary<Vector2I, Node3D> _plantedObjects = new();
	private readonly Dictionary<Vector2I, int> _plantedTiers = new();

	private Vector2I? _currentHoveredCell;
	private Vector2I? _heldCell;
	private ulong _pressTimestamp = 0;
	private bool _isDragHolding = false;

	public override void _Ready()
	{
		TileSelector ??= GetNodeOrNull<Node3D>("../TileSelector");
		if (TileSelector != null) TileSelector.Visible = false;

		HUDNode ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;
	}

	public override void _Process(double delta)
	{
		UpdateMouseHover();
		UpdateHeldFlowerVisual((float)delta);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton btn)
		{
			if (btn.ButtonIndex == MouseButton.Left)
			{
				if (btn.Pressed)
				{
					OnLeftClickPressed();
				}
				else
				{
					OnLeftClickReleased();
				}
			}
			else if (btn.ButtonIndex == MouseButton.Right && btn.Pressed)
			{
				OnRightClickPressed();
			}
		}
	}

	private void UpdateMouseHover()
	{
		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null) return;

		Vector3 rayOrigin = cam.GlobalPosition;
		Vector3 rayDir = -cam.GlobalTransform.Basis.Z.Normalized();

		var hit = GetWorld3D().DirectSpaceState.IntersectRay(new PhysicsRayQueryParameters3D
		{
			From = rayOrigin,
			To = rayOrigin + rayDir * 50f
		});

		if (hit.Count > 0 && WorldToGrid((Vector3)hit["position"]) is var cell && IsInsideGrid(cell))
		{
			_currentHoveredCell = cell;
			if (TileSelector != null)
			{
				TileSelector.Visible = true;
				TileSelector.GlobalPosition = GridToWorld(cell) + new Vector3(0, 0.05f, 0);
			}

			UpdateHoverPrompt(cell);
			return;
		}

		_currentHoveredCell = null;
		if (TileSelector != null) TileSelector.Visible = false;

		if (_heldCell.HasValue)
		{
			HUDNode?.SetInteractionPrompt("[RMB] Cancel Drag");
		}
	}

	private void UpdateHoverPrompt(Vector2I cell)
	{
		if (_heldCell.HasValue)
		{
			int heldTier = _plantedTiers.GetValueOrDefault(_heldCell.Value, 1);
			if (cell == _heldCell.Value)
			{
				HUDNode?.SetInteractionPrompt($"[LMB] Put down Tier {heldTier} Flower | [RMB] Cancel");
			}
			else if (_gridStates.GetValueOrDefault(cell) == TileState.Planted)
			{
				int targetTier = _plantedTiers.GetValueOrDefault(cell, 1);
				if (targetTier == heldTier && targetTier < 5)
				{
					HUDNode?.SetInteractionPrompt($"[★ MERGE!] Tier {heldTier} + Tier {heldTier} -> Tier {heldTier + 1}! (+{TierPoints[heldTier]} pts)");
				}
				else
				{
					HUDNode?.SetInteractionPrompt($"[LMB] Swap with Tier {targetTier} Flower | [RMB] Cancel");
				}
			}
			else
			{
				HUDNode?.SetInteractionPrompt($"[LMB] Move Tier {heldTier} Flower here | [RMB] Cancel");
			}
			return;
		}

		var state = _gridStates.GetValueOrDefault(cell, TileState.Empty);
		if (state == TileState.Empty)
		{
			HUDNode?.SetInteractionPrompt("[LMB] Till Soil");
		}
		else if (state == TileState.Tilled)
		{
			HUDNode?.SetInteractionPrompt("[LMB] Plant Tier 1 Flower (Daisy)");
		}
		else if (state == TileState.Planted)
		{
			int tier = _plantedTiers.GetValueOrDefault(cell, 1);
			HUDNode?.SetInteractionPrompt($"[LMB] Grab Tier {tier} Flower ({TierPoints[tier - 1]} pts) to Drag & Merge | [RMB] Remove");
		}
	}

	private void UpdateHeldFlowerVisual(float dt)
	{
		if (!_heldCell.HasValue || !_plantedObjects.TryGetValue(_heldCell.Value, out var heldObj) || heldObj == null)
			return;

		Vector3 targetPos = _currentHoveredCell.HasValue
			? GridToWorld(_currentHoveredCell.Value) + new Vector3(0, 1.8f, 0)
			: GridToWorld(_heldCell.Value) + new Vector3(0, 1.8f, 0);

		heldObj.GlobalPosition = heldObj.GlobalPosition.Lerp(targetPos, 20.0f * dt);
	}

	private void OnLeftClickPressed()
	{
		if (!_currentHoveredCell.HasValue) return;
		Vector2I cell = _currentHoveredCell.Value;

		if (_heldCell.HasValue)
		{
			// Second click in click-to-pick / click-to-place mode
			ExecuteDrop(cell);
			return;
		}

		var state = _gridStates.GetValueOrDefault(cell, TileState.Empty);
		if (state == TileState.Planted)
		{
			// Pick up flower to start drag / merge
			_heldCell = cell;
			_pressTimestamp = Time.GetTicksMsec();
			_isDragHolding = true;
		}
		else if (state == TileState.Empty)
		{
			_gridStates[cell] = TileState.Tilled;
			HUDNode?.SetInteractionPrompt("[LMB] Plant Tier 1 Flower (Daisy)");
		}
		else if (state == TileState.Tilled)
		{
			PlantCropAt(cell, 1);
		}
	}

	private void OnLeftClickReleased()
	{
		if (!_heldCell.HasValue || !_isDragHolding) return;
		_isDragHolding = false;

		ulong heldDuration = Time.GetTicksMsec() - _pressTimestamp;

		// If dragged to another cell or held longer than click threshold, execute drop
		if (heldDuration > 220 || (_currentHoveredCell.HasValue && _currentHoveredCell.Value != _heldCell.Value))
		{
			if (_currentHoveredCell.HasValue)
			{
				ExecuteDrop(_currentHoveredCell.Value);
			}
			else
			{
				CancelHeldFlower();
			}
		}
		// Otherwise, keep holding for click-to-place style
	}

	private void OnRightClickPressed()
	{
		if (_heldCell.HasValue)
		{
			CancelHeldFlower();
			return;
		}

		if (_currentHoveredCell.HasValue)
		{
			RemoveCrop(_currentHoveredCell.Value);
		}
	}

	private void ExecuteDrop(Vector2I targetCell)
	{
		if (!_heldCell.HasValue) return;
		Vector2I fromCell = _heldCell.Value;

		if (targetCell == fromCell)
		{
			ResetFlowerPosition(fromCell);
			_heldCell = null;
			return;
		}

		var targetState = _gridStates.GetValueOrDefault(targetCell, TileState.Empty);

		if (targetState == TileState.Planted)
		{
			int tierFrom = _plantedTiers.GetValueOrDefault(fromCell, 1);
			int tierTo = _plantedTiers.GetValueOrDefault(targetCell, 1);

			if (tierFrom == tierTo && tierFrom < 5)
			{
				// MERGE FORMULA: Level N + Level N = Level N + 1
				int newTier = tierFrom + 1;

				// Remove old dragged flower
				if (_plantedObjects.Remove(fromCell, out var oldObj)) oldObj.QueueFree();
				_plantedTiers.Remove(fromCell);
				_gridStates[fromCell] = TileState.Tilled;

				// Upgrade target flower
				if (_plantedObjects.Remove(targetCell, out var targetOld)) targetOld.QueueFree();
				Node3D upgradedFlower = CreateFlowerInstance(newTier);
				AddChild(upgradedFlower);
				upgradedFlower.GlobalPosition = GridToWorld(targetCell);

				_plantedObjects[targetCell] = upgradedFlower;
				_plantedTiers[targetCell] = newTier;

				// Visual merge pulse tween
				upgradedFlower.Scale = Vector3.One * 0.4f;
				Tween tw = CreateTween();
				tw.TweenProperty(upgradedFlower, "scale", Vector3.One * 1.35f, 0.12f);
				tw.TweenProperty(upgradedFlower, "scale", Vector3.One, 0.15f);

				HUDNode?.SetInteractionPrompt($"[★ MERGED!] Level {newTier} Flower created! (+{TierPoints[newTier - 1]} pts)");
				GD.Print($"[Node3d] MERGED: Level {tierFrom} + Level {tierFrom} -> Level {newTier} at {targetCell}");
				SaveManager.RecordMerge();
			}
			else
			{
				// SWAP: Different tiers or max tier
				var objFrom = _plantedObjects[fromCell];
				var objTo = _plantedObjects[targetCell];

				_plantedObjects[fromCell] = objTo;
				_plantedObjects[targetCell] = objFrom;

				_plantedTiers[fromCell] = tierTo;
				_plantedTiers[targetCell] = tierFrom;

				objFrom.GlobalPosition = GridToWorld(targetCell);
				objTo.GlobalPosition = GridToWorld(fromCell);

				HUDNode?.SetInteractionPrompt("Swapped flower positions.");
			}
		}
		else
		{
			// MOVE: Target is Empty or Tilled
			var obj = _plantedObjects[fromCell];
			int tier = _plantedTiers[fromCell];

			_plantedObjects.Remove(fromCell);
			_plantedTiers.Remove(fromCell);
			_gridStates[fromCell] = TileState.Tilled;

			_gridStates[targetCell] = TileState.Planted;
			_plantedObjects[targetCell] = obj;
			_plantedTiers[targetCell] = tier;

			obj.GlobalPosition = GridToWorld(targetCell);
			HUDNode?.SetInteractionPrompt($"Moved Tier {tier} flower to {targetCell}.");
		}

		_heldCell = null;
	}

	private void CancelHeldFlower()
	{
		if (_heldCell.HasValue)
		{
			ResetFlowerPosition(_heldCell.Value);
			_heldCell = null;
			HUDNode?.ClearInteractionPrompt();
		}
	}

	private void ResetFlowerPosition(Vector2I cell)
	{
		if (_plantedObjects.TryGetValue(cell, out var obj) && obj != null)
		{
			obj.GlobalPosition = GridToWorld(cell);
		}
	}

	private void PlantCropAt(Vector2I cell, int level = 1)
	{
		level = Mathf.Clamp(level, 1, 5);
		_gridStates[cell] = TileState.Planted;
		_plantedTiers[cell] = level;

		Node3D crop = CropPrefab?.Instantiate<Node3D>() ?? CreateFlowerInstance(level);
		AddChild(crop);
		crop.GlobalPosition = GridToWorld(cell);
		_plantedObjects[cell] = crop;

		HUDNode?.SetInteractionPrompt($"Planted Tier {level} Flower! (+{TierPoints[level - 1]} pts)");
	}

	private Node3D CreateFlowerInstance(int level)
	{
		level = Mathf.Clamp(level, 1, 5);
		Node3D root = new() { Name = $"Flower_T{level}" };

		float scaleMultiplier = TierScales[level - 1];
		Color blossomColor = TierColors[level - 1];

		// Stem Mesh
		var stemMesh = new MeshInstance3D
		{
			Name = "Stem",
			Mesh = new CylinderMesh
			{
				TopRadius = 0.15f * scaleMultiplier,
				BottomRadius = 0.20f * scaleMultiplier,
				Height = 1.8f * scaleMultiplier
			},
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.15f, 0.55f, 0.18f),
				Roughness = 0.6f
			},
			Position = new Vector3(0, (1.8f * scaleMultiplier) / 2.0f, 0)
		};
		root.AddChild(stemMesh);

		// Blossom Center
		var blossomMaterial = new StandardMaterial3D
		{
			AlbedoColor = blossomColor,
			Roughness = 0.35f
		};

		if (level == 5)
		{
			blossomMaterial.EmissionEnabled = true;
			blossomMaterial.Emission = new Color(0.08f, 0.55f, 0.25f);
			blossomMaterial.EmissionEnergyMultiplier = 1.5f;
		}

		var blossomMesh = new MeshInstance3D
		{
			Name = "Blossom",
			Mesh = new SphereMesh
			{
				Radius = 0.65f * scaleMultiplier,
				Height = 1.0f * scaleMultiplier
			},
			MaterialOverride = blossomMaterial,
			Position = new Vector3(0, 1.8f * scaleMultiplier + (0.3f * scaleMultiplier), 0)
		};
		root.AddChild(blossomMesh);

		// Petal Ring
		var petalRing = new MeshInstance3D
		{
			Name = "Petals",
			Mesh = new TorusMesh
			{
				InnerRadius = 0.25f * scaleMultiplier,
				OuterRadius = 0.80f * scaleMultiplier
			},
			MaterialOverride = blossomMaterial,
			Position = new Vector3(0, 1.8f * scaleMultiplier + (0.15f * scaleMultiplier), 0)
		};
		root.AddChild(petalRing);

		root.SetMeta("Level", level);
		return root;
	}

	private void RemoveCrop(Vector2I cell)
	{
		if (_gridStates.GetValueOrDefault(cell, TileState.Empty) != TileState.Empty)
		{
			if (_plantedObjects.Remove(cell, out var obj)) obj.QueueFree();
			_plantedTiers.Remove(cell);
			_gridStates[cell] = TileState.Empty;
			HUDNode?.SetInteractionPrompt("Removed crop.");
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

	public int GetPlantedCropCount() => _gridStates.Values.Count(s => s == TileState.Planted);

	public Dictionary<int, int> GetFlowerTierCounts()
	{
		var tierCounts = new Dictionary<int, int> { { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 } };
		foreach (var kvp in _plantedTiers)
		{
			int level = Mathf.Clamp(kvp.Value, 1, 5);
			tierCounts[level]++;
		}
		return tierCounts;
	}
}
