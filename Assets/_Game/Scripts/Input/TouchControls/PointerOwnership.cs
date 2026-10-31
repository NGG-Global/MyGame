namespace OpenSkies.FlightInput.TouchControls
{
    /// <summary>How a pointer was produced, so its liveness can be checked against the device.</summary>
    public enum PointerKind
    {
        Unknown = 0,
        Touch = 1,
        MouseOrPen = 2,
    }

    /// <summary>A control that can own pointers.</summary>
    public interface IPointerOwner
    {
        /// <summary>Shown in diagnostics.</summary>
        string OwnerName { get; }

        /// <summary>The pointer was taken away (focus loss, cancellation, control hidden). Release it and go neutral.</summary>
        void OnPointerRevoked(int pointerId);
    }

    /// <summary>
    /// Single source of truth for which control owns each active pointer.
    /// A pointer is claimed on press and keeps its owner until release or
    /// revocation, whatever it moves over. Anything else that reacts to
    /// pointers (camera look, cockpit instruments) must ignore owned pointers.
    /// Fixed capacity, no allocations after construction.
    /// </summary>
    public sealed class PointerOwnership
    {
        public const int Capacity = 16;

        readonly int[] _ids = new int[Capacity];
        readonly IPointerOwner[] _owners = new IPointerOwner[Capacity];
        readonly PointerKind[] _kinds = new PointerKind[Capacity];
        readonly int[] _touchIds = new int[Capacity];
        int _count;

        public int Count => _count;

        /// <summary>Claims a free pointer. Fails if another owner holds it or the table is full.</summary>
        public bool TryClaim(int pointerId, IPointerOwner owner, PointerKind kind = PointerKind.Unknown, int touchId = 0)
        {
            if (owner == null) return false;
            int index = IndexOf(pointerId);
            if (index >= 0) return ReferenceEquals(_owners[index], owner);
            if (_count == Capacity) return false;

            _ids[_count] = pointerId;
            _owners[_count] = owner;
            _kinds[_count] = kind;
            _touchIds[_count] = touchId;
            _count++;
            return true;
        }

        /// <summary>Releases a pointer, but only for its owner.</summary>
        public bool Release(int pointerId, IPointerOwner owner)
        {
            int index = IndexOf(pointerId);
            if (index < 0 || !ReferenceEquals(_owners[index], owner)) return false;
            RemoveAt(index);
            return true;
        }

        public bool IsOwned(int pointerId) => IndexOf(pointerId) >= 0;

        public bool IsOwnedBy(int pointerId, IPointerOwner owner)
        {
            int index = IndexOf(pointerId);
            return index >= 0 && ReferenceEquals(_owners[index], owner);
        }

        public IPointerOwner OwnerOf(int pointerId)
        {
            int index = IndexOf(pointerId);
            return index >= 0 ? _owners[index] : null;
        }

        /// <summary>Removes the pointer and tells its owner to go neutral.</summary>
        public void Revoke(int pointerId)
        {
            int index = IndexOf(pointerId);
            if (index < 0) return;
            var owner = _owners[index];
            RemoveAt(index);
            owner.OnPointerRevoked(pointerId);
        }

        /// <summary>Focus loss, pause, scene changes: every control goes neutral.</summary>
        public void RevokeAll()
        {
            while (_count > 0) Revoke(_ids[_count - 1]);
        }

        /// <summary>Releases everything held by one owner (e.g. the control was hidden or disabled).</summary>
        public void RevokeAllFrom(IPointerOwner owner)
        {
            for (int i = _count - 1; i >= 0; i--)
                if (ReferenceEquals(_owners[i], owner)) Revoke(_ids[i]);
        }

        // Read access by index for diagnostics and the liveness check, allocation-free.
        public int PointerIdAt(int index) => _ids[index];
        public IPointerOwner OwnerAt(int index) => _owners[index];
        public PointerKind KindAt(int index) => _kinds[index];
        public int TouchIdAt(int index) => _touchIds[index];

        int IndexOf(int pointerId)
        {
            for (int i = 0; i < _count; i++)
                if (_ids[i] == pointerId) return i;
            return -1;
        }

        void RemoveAt(int index)
        {
            int last = _count - 1;
            _ids[index] = _ids[last];
            _owners[index] = _owners[last];
            _kinds[index] = _kinds[last];
            _touchIds[index] = _touchIds[last];
            _owners[last] = null;
            _count = last;
        }
    }
}
