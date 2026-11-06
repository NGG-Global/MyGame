using System;
using UnityEngine;

namespace OpenSkies.Gestures
{
    /// <summary>Where a transient surface is in its life, as the coordinator sees it.</summary>
    public enum DisclosureState
    {
        Closed = 0,
        /// <summary>Opened; its appearance is still under way (the transition time).</summary>
        Opening = 1,
        Open = 2,
        /// <summary>Closed; its disappearance is still under way.</summary>
        Closing = 3,
    }

    /// <summary>Why a transient surface was told to collapse.</summary>
    public enum DisclosureCollapseReason
    {
        None = 0,
        /// <summary>A tap clearly outside the active branch.</summary>
        OutsideTap = 1,
        /// <summary>A tap on an ancestor while a child was open: back to that ancestor.</summary>
        ParentTap = 2,
        /// <summary>Its own trigger was tapped again.</summary>
        Trigger = 3,
        /// <summary>Another cluster opened in its place.</summary>
        Replaced = 4,
        /// <summary>The application lost focus.</summary>
        FocusLost = 5,
        /// <summary>A new flight or a scene transition.</summary>
        Reset = 6,
        /// <summary>A modal took over (the flight froze, an entry screen opened).</summary>
        Blocked = 7,
        /// <summary>Its owner closed it itself (a choice was made, the camera changed).</summary>
        Owner = 8,
    }

    /// <summary>
    /// The one place that knows which transient interface the player has expanded.
    /// Owners register a surface (a name, a screen hit test, a collapse callback, a
    /// parent and whether an outside tap may dismiss it) and tell the coordinator
    /// when they open or close it; the coordinator keeps the active branch (root to
    /// deepest), replaces a cluster when another opens, collapses a branch on an
    /// outside tap, a parent tap, focus loss or a reset, and reports opening, open,
    /// closing and closed states.
    ///
    /// It decides nothing about eligibility: the flight context (what the phase of
    /// flight offers) stays with <c>DisclosureController</c> in the awareness layer.
    /// This class manages player-selected expansion only. It touches no aircraft
    /// state and no pointer ownership; the <see cref="InteractionSurface"/> consults it
    /// for presses and consumes the dismissing tap. Pure C#, fixed capacity, no
    /// allocations after registration.
    /// </summary>
    public sealed class DisclosureCoordinator
    {
        public const int Capacity = 16;
        public const int None = -1;

        struct Layer
        {
            public bool Used;
            public string Name;
            public int Parent;
            public Func<Vector2, bool> Contains;
            public Action<DisclosureCollapseReason> Collapse;
            public bool DismissOnOutsideTap;
            public DisclosureState State;
            public float Remaining;
            public DisclosureCollapseReason LastReason;
        }

        readonly Layer[] _layers = new Layer[Capacity];
        readonly int[] _branch = new int[Capacity]; // root first, deepest last
        readonly int[] _closing = new int[Capacity * 2]; // surfaces being collapsed, so a callback that opens something never has it closed too
        int _depth, _closingCount;
        float _transitionSeconds = 0.2f;

        /// <summary>A surface changed state (its id and the new state). Views and sessions listen here.</summary>
        public event Action<int, DisclosureState> StateChanged;

        /// <summary>How long opening and closing take before the state settles (the fade time; 0 with reduced motion).</summary>
        public float TransitionSeconds
        {
            get => _transitionSeconds;
            set => _transitionSeconds = float.IsNaN(value) ? 0f : Mathf.Max(0f, value);
        }

        /// <summary>Number of open surfaces in the active branch (0: the quiet HUD).</summary>
        public int Depth => _depth;
        public bool HasActive => _depth > 0;
        public int ActiveRoot => _depth > 0 ? _branch[0] : None;
        public int Deepest => _depth > 0 ? _branch[_depth - 1] : None;
        /// <summary>The surface at a depth of the branch (0 = root), or None.</summary>
        public int LayerAt(int depth) => depth >= 0 && depth < _depth ? _branch[depth] : None;

        public bool IsRegistered(int id) => id >= 0 && id < Capacity && _layers[id].Used;
        public bool IsActive(int id) => IndexInBranch(id) >= 0;
        public DisclosureState StateOf(int id) => IsRegistered(id) ? _layers[id].State : DisclosureState.Closed;
        public string NameOf(int id) => IsRegistered(id) ? _layers[id].Name : null;
        public int ParentOf(int id) => IsRegistered(id) ? _layers[id].Parent : None;
        public bool DismissesOnOutsideTap(int id) => IsRegistered(id) && _layers[id].DismissOnOutsideTap;
        public DisclosureCollapseReason LastReasonOf(int id) => IsRegistered(id) ? _layers[id].LastReason : DisclosureCollapseReason.None;

        /// <summary>
        /// Registers a transient surface. <paramref name="contains"/> says whether a screen
        /// point is inside it (null: nothing is inside, every tap is outside);
        /// <paramref name="collapse"/> is called when the coordinator closes it (the owner
        /// hides its view). With <paramref name="dismissOnOutsideTap"/> off the surface stays
        /// through outside taps (a paused interface: leaving it is a deliberate action).
        /// Returns the id, or None when the table is full.
        /// </summary>
        public int Register(string name, Func<Vector2, bool> contains, Action<DisclosureCollapseReason> collapse,
            bool dismissOnOutsideTap = true, int parent = None)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (_layers[i].Used) continue;
                _layers[i] = new Layer
                {
                    Used = true,
                    Name = name ?? string.Empty,
                    Parent = IsRegistered(parent) ? parent : None,
                    Contains = contains,
                    Collapse = collapse,
                    DismissOnOutsideTap = dismissOnOutsideTap,
                    State = DisclosureState.Closed,
                };
                return i;
            }
            return None;
        }

        /// <summary>Forgets a surface. If it was open it leaves the branch without a collapse callback (its owner is going away).</summary>
        public void Unregister(int id)
        {
            if (!IsRegistered(id)) return;
            NotifyClosed(id);
            for (int i = 0; i < Capacity; i++)
                if (_layers[i].Used && _layers[i].Parent == id) _layers[i].Parent = None;
            _layers[id] = default;
        }

        /// <summary>Changes the parent of a surface (the settings open over the pause card or over the entry screen).</summary>
        public void SetParent(int id, int parent)
        {
            if (!IsRegistered(id) || parent == id) return;
            _layers[id].Parent = IsRegistered(parent) ? parent : None;
        }

        /// <summary>Forgets every surface (scene teardown).</summary>
        public void Clear()
        {
            _depth = 0;
            for (int i = 0; i < Capacity; i++) _layers[i] = default;
        }

        // ---- Opening and closing -----------------------------------------------------------------

        /// <summary>
        /// The owner opened a surface. Under its parent (when the parent is open) it replaces
        /// any sibling branch; as a root it replaces every other cluster. Opening an already
        /// open surface closes its descendants (back to it). Returns whether it is active.
        /// </summary>
        public bool Open(int id)
        {
            if (!IsRegistered(id)) return false;
            int existing = IndexInBranch(id);
            if (existing >= 0)
            {
                CloseAbove(existing, DisclosureCollapseReason.Replaced);
                return true;
            }
            int parentIndex = _layers[id].Parent != None ? IndexInBranch(_layers[id].Parent) : -1;
            if (parentIndex >= 0) CloseAbove(parentIndex, DisclosureCollapseReason.Replaced);
            else CloseAbove(-1, DisclosureCollapseReason.Replaced); // a new root: the previous cluster goes
            if (_depth >= Capacity) return false;
            _branch[_depth++] = id;
            _layers[id].LastReason = DisclosureCollapseReason.None;
            SetState(id, _transitionSeconds > 0f ? DisclosureState.Opening : DisclosureState.Open);
            return true;
        }

        /// <summary>Opens a closed surface, or closes an open one (its trigger was tapped again).</summary>
        public bool Toggle(int id)
        {
            if (IsActive(id))
            {
                Close(id, DisclosureCollapseReason.Trigger);
                return false;
            }
            return Open(id);
        }

        /// <summary>Closes a surface and everything open under it, deepest first, through the owners' collapse callbacks.</summary>
        public void Close(int id, DisclosureCollapseReason reason = DisclosureCollapseReason.Owner)
        {
            int index = IndexInBranch(id);
            if (index < 0) return;
            CloseAbove(index - 1, reason);
        }

        /// <summary>
        /// The owner closed a surface itself (a choice was made, the camera changed): it leaves
        /// the branch without a collapse callback; surfaces open under it are collapsed.
        /// </summary>
        public void NotifyClosed(int id)
        {
            int index = IndexInBranch(id);
            if (index < 0) return;
            CloseAbove(index, DisclosureCollapseReason.Owner);
            index = IndexInBranch(id); // a collapsed child may have opened something else meanwhile
            if (index >= 0)
            {
                for (int d = index; d < _depth - 1; d++) _branch[d] = _branch[d + 1];
                _depth--;
            }
            _layers[id].LastReason = DisclosureCollapseReason.Owner;
            SetState(id, _transitionSeconds > 0f ? DisclosureState.Closing : DisclosureState.Closed);
        }

        /// <summary>The owner's appearance finished early (reduced motion, an instant view): the state settles now.</summary>
        public void NotifyOpened(int id)
        {
            if (IsRegistered(id) && _layers[id].State == DisclosureState.Opening) SetState(id, DisclosureState.Open);
        }

        /// <summary>Closes the whole branch, deepest first, whatever its surfaces allow (a reset, a modal taking over).</summary>
        public int CollapseAll(DisclosureCollapseReason reason)
        {
            int closed = _depth;
            CloseAbove(-1, reason);
            return closed;
        }

        /// <summary>Closes from the deepest surface up to, not including, the first one that outside taps may not dismiss.</summary>
        public int CollapseDismissable(DisclosureCollapseReason reason) => CloseDismissableAbove(-1, reason);

        /// <summary>
        /// Focus loss: flight-time transients fold (a branch whose root an outside tap could
        /// dismiss); a paused interface is left as it is, so coming back finds it unchanged.
        /// </summary>
        public void OnFocusLost()
        {
            if (_depth > 0 && _layers[_branch[0]].DismissOnOutsideTap) CollapseAll(DisclosureCollapseReason.FocusLost);
        }

        /// <summary>A new flight or a scene transition: everything collapses.</summary>
        public void Reset() => CollapseAll(DisclosureCollapseReason.Reset);

        // ---- Outside taps ---------------------------------------------------------------------------

        /// <summary>
        /// Whether a press at a screen point is outside the active branch in a way a tap
        /// would act on: outside every open surface (the dismissable ones collapse), or on an
        /// ancestor while a child is open (back to the ancestor). Inside the deepest surface,
        /// or with nothing dismissable, the press belongs to whoever is under it.
        /// </summary>
        public bool WantsOutsideTap(Vector2 screenPoint)
        {
            if (_depth == 0) return false;
            int containing = DeepestContaining(screenPoint);
            if (containing == _depth - 1) return false;
            return _layers[_branch[_depth - 1]].DismissOnOutsideTap;
        }

        /// <summary>
        /// A tap outside the active branch: collapses it from the deepest surface down to the one
        /// the tap landed on (an ancestor) or, on empty space, down to the first surface that
        /// outside taps may not dismiss. Returns how many surfaces closed.
        /// </summary>
        public int OutsideTap(Vector2 screenPoint)
        {
            if (_depth == 0) return 0;
            int containing = DeepestContaining(screenPoint);
            if (containing == _depth - 1) return 0;
            return CloseDismissableAbove(containing, containing >= 0 ? DisclosureCollapseReason.ParentTap : DisclosureCollapseReason.OutsideTap);
        }

        /// <summary>The deepest open surface containing a screen point, as a depth in the branch, or -1.</summary>
        public int DeepestContaining(Vector2 screenPoint)
        {
            for (int d = _depth - 1; d >= 0; d--)
            {
                var contains = _layers[_branch[d]].Contains;
                if (contains != null && contains(screenPoint)) return d;
            }
            return -1;
        }

        // ---- Time -----------------------------------------------------------------------------------

        /// <summary>Settles Opening into Open and Closing into Closed after the transition time.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (!(unscaledDeltaTime > 0f)) return;
            for (int i = 0; i < Capacity; i++)
            {
                if (!_layers[i].Used) continue;
                var state = _layers[i].State;
                if (state != DisclosureState.Opening && state != DisclosureState.Closing) continue;
                _layers[i].Remaining -= unscaledDeltaTime;
                if (_layers[i].Remaining > 0f) continue;
                SetState(i, state == DisclosureState.Opening ? DisclosureState.Open : DisclosureState.Closed);
            }
        }

        // ---- Internals ------------------------------------------------------------------------------

        int IndexInBranch(int id)
        {
            for (int d = 0; d < _depth; d++)
                if (_branch[d] == id) return d;
            return -1;
        }

        /// <summary>Closes the dismissable surfaces deeper than a depth, deepest first, stopping at the first one that stays. Returns how many.</summary>
        int CloseDismissableAbove(int depth, DisclosureCollapseReason reason)
        {
            int keep = _depth - 1;
            while (keep > depth && _layers[_branch[keep]].DismissOnOutsideTap) keep--;
            int closed = _depth - 1 - keep;
            CloseAbove(keep, reason);
            return closed;
        }

        /// <summary>
        /// Closes every surface deeper than a depth (-1: all), deepest first. The branch is cut
        /// before any owner is told, so an owner that opens or closes something else in response
        /// sees a consistent branch, and nothing it opens is closed by this same call.
        /// </summary>
        void CloseAbove(int depth, DisclosureCollapseReason reason)
        {
            int target = Mathf.Max(0, depth + 1);
            if (_depth <= target) return;
            int start = _closingCount;
            int count = Mathf.Min(_depth - target, _closing.Length - start);
            for (int i = 0; i < count; i++) _closing[start + i] = _branch[_depth - 1 - i];
            _closingCount = start + count;
            _depth = target;
            for (int i = 0; i < count; i++)
            {
                int id = _closing[start + i];
                _layers[id].LastReason = reason;
                SetState(id, _transitionSeconds > 0f ? DisclosureState.Closing : DisclosureState.Closed);
                _layers[id].Collapse?.Invoke(reason);
            }
            _closingCount = start;
        }

        void SetState(int id, DisclosureState state)
        {
            if (!_layers[id].Used || _layers[id].State == state) return;
            _layers[id].State = state;
            _layers[id].Remaining = state == DisclosureState.Opening || state == DisclosureState.Closing ? _transitionSeconds : 0f;
            StateChanged?.Invoke(id, state);
        }
    }
}
