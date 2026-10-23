using Godot;

public partial class InteractionRaycaster : Node3D
{
	[Signal]
	public delegate void TargetChangedEventHandler(Node3D newTarget);

	[Export] public float InteractionRange = 6.0f;
	[Export] public PlayerController Player;
	[Export] public HUDManager HUD;

	public IInteractable CurrentInteractable { get; private set; }
	public Node3D CurrentTargetNode { get; private set; }

	public override void _Ready()
	{
		if (Player == null)
		{
			Player = GetParent() as PlayerController;
		}

		if (HUD == null)
		{
			HUD = GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		}
	}

	public override void _Process(double delta)
	{
		CheckInteractable();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (CurrentInteractable == null || Player == null)
			return;

		if (!CurrentInteractable.CanInteract(Player))
			return;

		// Key.E to interact
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.E)
			{
				CurrentInteractable.Interact(Player);
				GetViewport().SetInputAsHandled();
			}
		}
		// Left Click to interact when hovering directly on the interactable
		else if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
		{
			if (IsMouseDirectlyOverCurrentTarget())
			{
				CurrentInteractable.Interact(Player);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void CheckInteractable()
	{
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
			return;

		Vector2 mousePos = GetViewport().GetMousePosition();
		Vector3 rayOrigin = camera.ProjectRayOrigin(mousePos);
		Vector3 rayDir = camera.ProjectRayNormal(mousePos);

		// Also check from player's eye level towards camera look direction if mouse isn't over an object
		Vector3 from = rayOrigin;
		Vector3 to = from + rayDir * 1000.0f;

		var spaceState = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(from, to);

		// Exclude player's own collider
		if (Player != null)
		{
			query.Exclude = new Godot.Collections.Array<Rid> { Player.GetRid() };
		}

		var result = spaceState.IntersectRay(query);
		IInteractable found = null;
		Node3D foundNode = null;

		if (result.Count > 0)
		{
			GodotObject collider = (GodotObject)result["collider"];
			Vector3 hitPos = (Vector3)result["position"];

			// Check distance from player
			float dist = Player != null ? Player.GlobalPosition.DistanceTo(hitPos) : GlobalPosition.DistanceTo(hitPos);

			if (dist <= InteractionRange)
			{
				found = collider as IInteractable;
				if (found == null && collider is Node node)
				{
					// Check parent or ancestors
					Node cur = node;
					while (cur != null && found == null)
					{
						found = cur as IInteractable;
						if (found != null)
						{
							foundNode = cur as Node3D;
							break;
						}
						cur = cur.GetParent();
					}
				}
				else if (found != null)
				{
					foundNode = collider as Node3D;
				}
			}
		}

		// Also check player facing direction and proximity if mouse ray didn't hit anything in range
		if (found == null && Player != null)
		{
			Vector3 playerFrom = Player.GlobalPosition + new Vector3(0, 1.0f, 0);
			Vector3 facingDir = Player.VisualRoot != null 
				? Player.VisualRoot.GlobalTransform.Basis.Z.Normalized()
				: -camera.GlobalTransform.Basis.Z.Normalized();

			Vector3 playerTo = playerFrom + facingDir * InteractionRange;
			var playerQuery = PhysicsRayQueryParameters3D.Create(playerFrom, playerTo);
			playerQuery.Exclude = new Godot.Collections.Array<Rid> { Player.GetRid() };

			var playerHit = spaceState.IntersectRay(playerQuery);
			if (playerHit.Count > 0)
			{
				GodotObject col = (GodotObject)playerHit["collider"];
				found = ExtractInteractable(col, out foundNode);
			}

			// Proximity sphere check if facing ray missed
			if (found == null)
			{
				var shapeQuery = new PhysicsShapeQueryParameters3D();
				var sphere = new SphereShape3D { Radius = 3.2f };
				shapeQuery.ShapeRid = sphere.GetRid();
				shapeQuery.Transform = new Transform3D(Basis.Identity, playerFrom);
				shapeQuery.Exclude = new Godot.Collections.Array<Rid> { Player.GetRid() };

				var hits = spaceState.IntersectShape(shapeQuery, 10);
				float closestDist = float.MaxValue;

				for (int i = 0; i < hits.Count; i++)
				{
					var hitDict = hits[i];
					GodotObject obj = (GodotObject)hitDict["collider"];
					var candidate = ExtractInteractable(obj, out Node3D cNode);
					if (candidate != null && candidate.CanInteract(Player))
					{
						float d = cNode != null ? playerFrom.DistanceTo(cNode.GlobalPosition) : 3.0f;
						if (d < closestDist)
						{
							closestDist = d;
							found = candidate;
							foundNode = cNode;
						}
					}
				}
			}
		}

		if (found != CurrentInteractable)
		{
			CurrentInteractable = found;
			CurrentTargetNode = foundNode;
			EmitSignal(SignalName.TargetChanged, CurrentTargetNode);
		}

		UpdatePrompt();
	}

	private bool IsMouseDirectlyOverCurrentTarget()
	{
		if (CurrentTargetNode == null)
			return false;

		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null) return false;

		Vector2 mousePos = GetViewport().GetMousePosition();
		Vector3 rayOrigin = camera.ProjectRayOrigin(mousePos);
		Vector3 to = rayOrigin + camera.ProjectRayNormal(mousePos) * 1000.0f;

		var spaceState = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(rayOrigin, to);
		if (Player != null) query.Exclude = new Godot.Collections.Array<Rid> { Player.GetRid() };

		var result = spaceState.IntersectRay(query);
		if (result.Count > 0)
		{
			GodotObject collider = (GodotObject)result["collider"];
			if (collider == CurrentTargetNode || (collider is Node node && node.IsAncestorOf(CurrentTargetNode) || CurrentTargetNode.IsAncestorOf(collider as Node)))
			{
				return true;
			}
		}
		return false;
	}

	private void UpdatePrompt()
	{
		if (HUD == null)
		{
			HUD = GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		}

		if (HUD != null)
		{
			if (CurrentInteractable != null && CurrentInteractable.CanInteract(Player))
			{
				HUD.SetInteractionPrompt(CurrentInteractable.GetPrompt());
			}
			else
			{
				HUD.ClearInteractionPrompt();
			}
		}
	}

	private IInteractable ExtractInteractable(GodotObject col, out Node3D foundNode)
	{
		foundNode = null;
		if (col == null)
			return null;

		IInteractable found = col as IInteractable;
		if (found == null && col is Node node)
		{
			Node cur = node;
			while (cur != null && found == null)
			{
				found = cur as IInteractable;
				if (found != null)
				{
					foundNode = cur as Node3D;
					break;
				}
				cur = cur.GetParent();
			}
		}
		else if (found != null)
		{
			foundNode = col as Node3D;
		}

		return found;
	}
}
