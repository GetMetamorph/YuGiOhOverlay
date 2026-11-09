using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Application;

public sealed record GuideSnapshot(long Revision, ComboRule? Combo, int StepIndex, bool AutoAdvance, bool Completed)
{
    public bool IsFollowing => Combo is not null;
    public VisualStepCue? Cue => !Completed ? Combo?.GuideSteps?.ElementAtOrDefault(StepIndex)?.Cue : null;
}

// Shared by the UI and observation worker. Revisions reject in-flight results from
// an earlier guide, step or mode; no capture or UI dependency lives here.
public sealed class GuideSession
{
    private readonly object _gate = new();
    private GuideSnapshot _state = new(0, null, 0, true, false);
    public GuideSnapshot Snapshot { get { lock (_gate) return _state; } }
    public void Follow(ComboRule combo)
    {
        if (combo.Actions.Count == 0) throw new ArgumentException("Combo sans étape.");
        lock (_gate) _state = new(_state.Revision + 1, combo, 0, _state.AutoAdvance, false);
    }
    public void Exit() { lock (_gate) _state = new(_state.Revision + 1, null, 0, _state.AutoAdvance, false); }
    public void RestartObservation() { lock (_gate) _state = _state with { Revision = _state.Revision + 1 }; }
    public void SetAutoAdvance(bool enabled)
    {
        lock (_gate) if (_state.AutoAdvance != enabled) _state = _state with { Revision = _state.Revision + 1, AutoAdvance = enabled };
    }
    public void Move(int delta)
    {
        lock (_gate)
        {
            if (_state.Combo is null) return;
            var index = Math.Clamp(_state.StepIndex + delta, 0, _state.Combo.Actions.Count - 1);
            _state = _state with { Revision = _state.Revision + 1, StepIndex = index, Completed = false };
        }
    }
    public bool TryAdvance(long revision)
    {
        lock (_gate)
        {
            if (_state.Revision != revision || _state.Combo is null || _state.Completed || !_state.AutoAdvance) return false;
            var last = _state.StepIndex == _state.Combo.Actions.Count - 1;
            _state = _state with { Revision = revision + 1, StepIndex = last ? _state.StepIndex : _state.StepIndex + 1, Completed = last };
            return true;
        }
    }
}
