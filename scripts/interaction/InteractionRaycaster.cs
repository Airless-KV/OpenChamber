// =========================================================================================
// Original Author : air (Airless-KV) - Oct 2026
// Responsibilities: - Camera and player raycaster for identifying IInteractable targets
//                   - HUD prompt integration and interaction input dispatching ([E] / Click)
//                   - Dynamic target selection and highlight management
// -----------------------------------------------------------------------------------------
// Change Log:
// - Oct 24, 2026 | air : Added viewport center raycast aiming support when mouse is captured in First-Person mode
// - Oct 24, 2026 | air : Streamlined raycasting queries and ancestor resolution for Alpha build
// - Oct 30, 2026 | air : Adapted raycaster strictly for First-Person crosshair center-ray targeting
// =========================================================================================
using Godot;

public partial class InteractionRaycaster : Node3D
{
	[Signal] public delegate void TargetChangedEventHandler(Node3D newTarget);

	[Export] public float InteractionRange = 6.0f;
	[Export] public PlayerController Player;
	[Export] public HUDManager HUD;

	public IInteractable CurrentInteractable { get; private set; }
	public Node3D CurrentTargetNode { get; private set; }

	public override void _Ready()
	{
		Player ??= GetParent() as PlayerController;
		HUD ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;
	}

	public override void _Process(double delta) => CheckInteractable();

	public override void _UnhandledInput(InputEvent @event)
	{
		if (CurrentInteractable == null || Player == null || !CurrentInteractable.CanInteract(Player))
			return;

		bool interactE = @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.E };
		bool interactClick = @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left };

		if (interactE || interactClick)
		{
			CurrentInteractable.Interact(Player);
			GetViewport().SetInputAsHandled();
		}
	}

	private void CheckInteractable()
	{
		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null) return;

		var space = GetWorld3D().DirectSpaceState;
		var exclude = Player != null ? new Godot.Collections.Array<Rid> { Player.GetRid() } : null;

		Vector3 rayOrigin = cam.GlobalPosition;
		Vector3 rayDir = -cam.GlobalTransform.Basis.Z.Normalized();

		var hit = space.IntersectRay(new PhysicsRayQueryParameters3D 
		{ 
			From = rayOrigin, 
			To = rayOrigin + rayDir * InteractionRange,
			Exclude = exclude 
		});

		IInteractable found = null;
		Node3D foundNode = null;

		if (hit.Count > 0)
		{
			found = ExtractInteractable((GodotObject)hit["collider"], out foundNode);
		}

		if (found != CurrentInteractable)
		{
			CurrentInteractable = found;
			CurrentTargetNode = foundNode;
			EmitSignal(SignalName.TargetChanged, CurrentTargetNode);
		}

		HUD ??= GetTree().Root.FindChild("HUD", true, false) as HUDManager;
		if (HUD != null)
		{
			if (CurrentInteractable != null && CurrentInteractable.CanInteract(Player))
				HUD.SetInteractionPrompt(CurrentInteractable.GetPrompt());
			else
				HUD.ClearInteractionPrompt();
		}
	}

	public static IInteractable ExtractInteractable(GodotObject col, out Node3D foundNode)
	{
		foundNode = null;
		for (Node cur = col as Node; cur != null; cur = cur.GetParent())
		{
			if (cur is IInteractable interactable)
			{
				foundNode = cur as Node3D;
				return interactable;
			}
		}
		return null;
	}
}
