using Godot;
using System.Collections.Generic;

public partial class Node3d : Godot.Node3D
{
	public enum TileState { Empty, Tilled, Planted }

	[ExportGroup("Grid Dimensions")]
	[Export] public int GridWidth = 8;          // 8x8 like a chessboard
	[Export] public int GridHeight = 8;
	[Export] public float CellSize = 6.0f;       // Size of each square in meters
	[Export] public Vector3 GridOrigin = new Vector3(-23.97f, 0.0f, -23.97f); // Center or top-left of board

	[ExportGroup("Scene References")]
	[Export] public Node3D TileSelector;        // The visual highlight box
	[Export] public PackedScene CropPrefab;     // PackedScene to spawn when planted

	// Tracks the state and planted entity at each (x, y) cell
	private Dictionary<Vector2I, TileState> _gridStates = new();
	private Dictionary<Vector2I, Node3D> _plantedObjects = new();

	private Vector2I? _currentHoveredCell = null;

	public override void _Ready()
	{
		// Initialize all tiles as Empty
		for (int x = 0; x < GridWidth; x++)
		{
			for (int y = 0; y < GridHeight; y++)
			{
				_gridStates[new Vector2I(x, y)] = TileState.Empty;
			}
		}
	}

	public override void _Process(double delta)
	{
		UpdateMouseHover();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
		{
			if (_currentHoveredCell.HasValue)
			{
				Vector2I cell = _currentHoveredCell.Value;

				// Left click: Till soil or Plant crop
				if (mouseBtn.ButtonIndex == MouseButton.Left)
				{
					HandleTileInteraction(cell);
				}
				// Right click: Remove/harvest crop
				else if (mouseBtn.ButtonIndex == MouseButton.Right)
				{
					RemoveCrop(cell);
				}
			}
		}
	}

	private void UpdateMouseHover()
	{
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		Vector2 mousePos = GetViewport().GetMousePosition();

		// 3D Raycasting from screen through the active camera
		Vector3 from = camera.ProjectRayOrigin(mousePos);
		Vector3 to = from + camera.ProjectRayNormal(mousePos) * 1000.0f;

		var spaceState = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(from, to);
		var hit = spaceState.IntersectRay(query);

		if (hit.Count > 0)
		{
			Vector3 hitPos = (Vector3)hit["position"];
			Vector2I cell = WorldToGrid(hitPos);

			if (IsInsideGrid(cell))
			{
				_currentHoveredCell = cell;

				if (TileSelector != null)
				{
					TileSelector.Visible = true;
					// Snap the selector exactly to the center of the hovered tile
					TileSelector.GlobalPosition = GridToWorld(cell) + new Vector3(0, 0.05f, 0);
				}
				return;
			}
		}

		// Mouse is outside grid boundaries or hitting nothing
		_currentHoveredCell = null;
		if (TileSelector != null)
		{
			TileSelector.Visible = false;
		}
	}

	private void HandleTileInteraction(Vector2I cell)
	{
		TileState state = _gridStates[cell];

		switch (state)
		{
			case TileState.Empty:
				_gridStates[cell] = TileState.Tilled;
				GD.Print($"Tilled soil at {cell}");
				break;

			case TileState.Tilled:
				PlantCropAt(cell);
				break;

			case TileState.Planted:
				GD.Print($"Cell {cell} already has a crop!");
				break;
		}
	}

	private void PlantCropAt(Vector2I cell)
	{
		_gridStates[cell] = TileState.Planted;

		if (CropPrefab != null)
		{
			Node3D cropInstance = CropPrefab.Instantiate<Node3D>();
			AddChild(cropInstance);
			cropInstance.GlobalPosition = GridToWorld(cell);
			_plantedObjects[cell] = cropInstance;
		}
		else
		{
			// Simple fallback: spawn a placeholder colored cube if no prefab is assigned
			MeshInstance3D pill = new MeshInstance3D
			{
				Mesh = new CapsuleMesh
				{
					Radius = 1.0f, 
					Height = 3.5f
				}
			};
			
			StandardMaterial3D pillMat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.2f, 0.75f, 0.25f),
				Roughness = 0.4f
			};
			pill.MaterialOverride = pillMat;
			
			AddChild(pill);
			Vector3 spawnPos = GridToWorld(cell);
			spawnPos.Y += 1.75f; // Half of height (3.5 / 2) to rest flush on the surface
			pill.GlobalPosition = spawnPos;
			
			_plantedObjects[cell] = pill;
		}
		GD.Print($"Planted pill crop at cell: {cell}");
	}

	private void RemoveCrop(Vector2I cell)
	{
		if (_gridStates[cell] != TileState.Empty)
		{
			if (_plantedObjects.ContainsKey(cell))
			{
				_plantedObjects[cell].QueueFree();
				_plantedObjects.Remove(cell);
			}
			_gridStates[cell] = TileState.Empty;
			GD.Print($"Cleared tile at {cell}");
		}
	}

	// Mathematical coordinate mapping
	public Vector2I WorldToGrid(Vector3 worldPos)
	{
		Vector3 offset = worldPos - GridOrigin;
		int x = Mathf.FloorToInt(offset.X / CellSize);
		int z = Mathf.FloorToInt(offset.Z / CellSize);
		return new Vector2I(x, z);
	}

	public Vector3 GridToWorld(Vector2I cell)
	{
		return new Vector3(
			GridOrigin.X + (cell.X + 0.5f) * CellSize,
			GridOrigin.Y,
			GridOrigin.Z + (cell.Y + 0.5f) * CellSize
		);
	}

	public bool IsInsideGrid(Vector2I cell)
	{
		return cell.X >= 0 && cell.X < GridWidth && cell.Y >= 0 && cell.Y < GridHeight;
	}
}
