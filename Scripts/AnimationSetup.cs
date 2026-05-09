using Godot;

// Builds the AnimationPlayer library and AnimationTree state machine at runtime (_Ready).
// Drop a Mixamo FBX into Assets/Animations/ and point IdleAnimationPath at it — bone track
// paths are remapped automatically so they match whatever SkeletonPath is set to.
public partial class AnimationSetup : Node3D
{
	// The AnimationTree condition name used by both this script and PlayerMovement.
	// Changing it here also changes the AnimationTree parameter path in PlayerMovement.
	public const string SwapStanceCondition = "swap_stance";

	// Set in the Inspector to the res:// path of a Mixamo idle FBX (e.g. BreathingIdle.fbx).
	// Leave empty to use a blank looping placeholder (T-pose).
	[Export] public string IdleAnimationPath = "";

	[Export] public float RaiseRearDuration = 0.4f;
	[Export] public float LowerRearDuration = 0.4f;

	// Must match the scene path from CharacterVisual to Skeleton3D.
	// Change this in the Inspector if you swap to a different rig.
	[Export] public string SkeletonPath = "XBot/Skeleton3D";

	public override void _Ready()
	{
		var animPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
		var animTree   = GetNodeOrNull<AnimationTree>("AnimationTree");
		if (animPlayer == null || animTree == null)
		{
			GD.PushWarning("AnimationSetup: AnimationPlayer or AnimationTree missing.");
			return;
		}

		BuildLibrary(animPlayer);
		BuildStateMachine(animTree);
	}

	// ── Library ───────────────────────────────────────────────────────────────

	void BuildLibrary(AnimationPlayer player)
	{
		var lib = new AnimationLibrary();

		Animation idle = TryLoadExternalAnimation(IdleAnimationPath) ?? MakeEmptyLooping(1f);
		lib.AddAnimation("Idle",      idle);
		lib.AddAnimation("RaiseRear", MakeOneShot(RaiseRearDuration));
		lib.AddAnimation("LowerRear", MakeLowerRear());

		player.AddAnimationLibrary("", lib);
		player.Play("Idle");
	}

	// Looping placeholder — no tracks, character stays at rest/T-pose.
	static Animation MakeEmptyLooping(float length)
	{
		var a = new Animation { Length = length, LoopMode = Animation.LoopModeEnum.Linear };
		return a;
	}

	// One-shot with no bone tracks. Add real keyframes in the AnimationPlayer editor.
	static Animation MakeOneShot(float length)
	{
		var a = new Animation { Length = length, LoopMode = Animation.LoopModeEnum.None };
		return a;
	}

	// LowerRear: one-shot with two method-track callbacks on Player (NodePath "..").
	// Frame 0   → OnStanceMidpoint     (flips CharacterVisual.Scale.X)
	// Last frame → OnStanceSwapComplete (clears _moving, re-enables input)
	Animation MakeLowerRear()
	{
		var a = new Animation { Length = LowerRearDuration, LoopMode = Animation.LoopModeEnum.None };

		int track = a.AddTrack(Animation.TrackType.Method);
		a.TrackSetPath(track, new NodePath(".."));
		a.TrackSetInterpolationType(track, Animation.InterpolationType.Nearest);

		a.TrackInsertKey(track, 0f, new Godot.Collections.Dictionary
		{
			{ "method", nameof(PlayerMovement.OnStanceMidpoint) },
			{ "args",   new Godot.Collections.Array() },
		});
		a.TrackInsertKey(track, LowerRearDuration - 0.001f, new Godot.Collections.Dictionary
		{
			{ "method", nameof(PlayerMovement.OnStanceSwapComplete) },
			{ "args",   new Godot.Collections.Array() },
		});

		return a;
	}

	// ── External animation loader ─────────────────────────────────────────────

	// Loads the first animation found in an FBX/GLTF PackedScene and remaps its
	// bone track paths so they match our skeleton at "XBot/Skeleton3D".
	// Works for Mixamo FBX exported with or without skin.
	Animation TryLoadExternalAnimation(string path)
	{
		if (string.IsNullOrEmpty(path)) return null;

		var scene = ResourceLoader.Load<PackedScene>(path);
		if (scene == null)
		{
			GD.PushWarning($"AnimationSetup: IdleAnimationPath not found: {path}");
			return null;
		}

		var instance = scene.Instantiate();
		try
		{
			Animation anim = ExtractFirstAnimation(instance);
			if (anim == null)
			{
				GD.PushWarning($"AnimationSetup: no animation found in {path}");
				return null;
			}
			return RemapBonePaths(anim.Duplicate() as Animation);
		}
		finally
		{
			instance.QueueFree();
		}
	}

	// Searches the tree rooted at node (node-first, then children) for an AnimationPlayer.
	static Animation ExtractFirstAnimation(Node node)
	{
		if (node is AnimationPlayer ap)
			return GetFirstClip(ap);
		foreach (Node child in node.GetChildren())
		{
			var found = ExtractFirstAnimation(child);
			if (found != null) return found;
		}
		return null;
	}

	static Animation GetFirstClip(AnimationPlayer ap)
	{
		foreach (StringName libName in ap.GetAnimationLibraryList())
		{
			var lib = ap.GetAnimationLibrary(libName);
			foreach (StringName animName in lib.GetAnimationList())
				return lib.GetAnimation(animName);
		}
		return null;
	}

	// Mixamo exports embed the skeleton under an "Armature" node, so bone track paths look
	// like "Armature/Skeleton3D:mixamorig_Hips". Our scene has the skeleton at SkeletonPath,
	// so we rewrite everything before the bone name to match.
	Animation RemapBonePaths(Animation anim)
	{
		const string skelToken = "Skeleton3D:";
		for (int i = 0; i < anim.GetTrackCount(); i++)
		{
			string path = anim.TrackGetPath(i).ToString();
			int idx = path.IndexOf(skelToken);
			if (idx < 0) continue;
			string bonePart = path.Substring(idx + skelToken.Length); // "mixamorig_Hips"
			anim.TrackSetPath(i, new NodePath($"{SkeletonPath}:{bonePart}"));
		}
		return anim;
	}

	// ── AnimationTree state machine ───────────────────────────────────────────

	static void BuildStateMachine(AnimationTree tree)
	{
		var sm = new AnimationNodeStateMachine();

		sm.AddNode("Idle",      new AnimationNodeAnimation { Animation = "Idle" },      new Vector2(  0, 0));
		sm.AddNode("RaiseRear", new AnimationNodeAnimation { Animation = "RaiseRear" }, new Vector2(200, 0));
		sm.AddNode("LowerRear", new AnimationNodeAnimation { Animation = "LowerRear" }, new Vector2(400, 0));

		// Idle → RaiseRear when parameters/conditions/swap_stance = true
		// Godot auto-resets the condition once the transition fires.
		sm.AddTransition("Idle", "RaiseRear",
			new AnimationNodeStateMachineTransition { AdvanceCondition = SwapStanceCondition });

		sm.AddTransition("RaiseRear", "LowerRear",
			new AnimationNodeStateMachineTransition
			{
				AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Auto,
			});

		sm.AddTransition("LowerRear", "Idle",
			new AnimationNodeStateMachineTransition
			{
				AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Auto,
			});

		tree.TreeRoot = sm;
		tree.Active   = true;
	}
}
