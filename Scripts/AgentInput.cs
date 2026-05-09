using Godot;

// Receives discrete action branches from the RL policy and exposes them as ICharacterInput.
// Branch 0: linear  — 0=None, 1=StepFwd, 2=StepBack, 3=DashFwd, 4=DashBack
// Branch 1: sidestep — 0=None, 1=Left, 2=Right
// Branch 2: swap    — 0=None, 1=Swap
// Branch 3: kick    — 0=None, 1=Kick
public partial class AgentInput : Node, ICharacterInput
{
	static readonly int[] LinearMap = { 0, 1, -1, 2, -2 };

	int  _linear;
	int  _sideStep;
	bool _swap;
	bool _kick;

	public int  LinearInput         { get { var v = _linear;   _linear   = 0; return v; } }
	public int  SideStepInput       { get { var v = _sideStep; _sideStep = 0; return v; } }
	public bool SwapStanceTriggered { get { var v = _swap;     _swap     = false; return v; } }
	public bool KickTriggered       { get { var v = _kick;     _kick     = false; return v; } }

	// Called by the RL bridge each step with the policy's chosen action indices.
	public void SetActions(int branchLinear, int branchSide, int branchSwap, int branchKick)
	{
		if (branchLinear >= 0 && branchLinear < LinearMap.Length)
			_linear = LinearMap[branchLinear];
		_sideStep = branchSide == 1 ? -1 : branchSide == 2 ? 1 : 0;
		if (branchSwap == 1) _swap = true;
		if (branchKick == 1) _kick = true;
	}
}
