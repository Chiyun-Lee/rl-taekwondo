// LinearInput:       -2=DashBack, -1=StepBack, 0=None, 1=StepForward, 2=DashForward
// SideStepInput:     -1=Left, 0=None, 1=Right
// SwapStanceTriggered: true for one tick when Space is pressed
// KickTriggered:       true for one tick when S is pressed
public interface ICharacterInput
{
    int  LinearInput         { get; }
    int  SideStepInput       { get; }
    bool SwapStanceTriggered { get; }
    bool KickTriggered       { get; }
}
