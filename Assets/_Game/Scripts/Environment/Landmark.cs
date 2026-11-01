using System;
using UnityEngine;

namespace OpenSkies.World
{
    /// <summary>What a landmark is. Values are stable.</summary>
    public enum LandmarkKind
    {
        Airfield = 0,
        Lighthouse = 1,
        Harbour = 2,
        Island = 3,
    }

    /// <summary>
    /// A named place a pilot can see and navigate to. Positions are world space,
    /// metres; only the horizontal position matters for navigation.
    /// </summary>
    [Serializable]
    public struct Landmark
    {
        [Tooltip("Stable identifier used by missions and saved data, lower case.")]
        public string id;
        [Tooltip("Short name for the HUD, upper case.")]
        public string displayName;
        public LandmarkKind kind;
        [Tooltip("World position of the landmark (ground level), m.")]
        public Vector3 position;
        [Tooltip("A pilot flying within this horizontal distance has reached it, m.")]
        public float arrivalRadiusMetres;

        public Landmark(string id, string displayName, LandmarkKind kind, Vector3 position, float arrivalRadiusMetres)
        {
            this.id = id;
            this.displayName = displayName;
            this.kind = kind;
            this.position = position;
            this.arrivalRadiusMetres = arrivalRadiusMetres;
        }

        public bool IsDefined => !string.IsNullOrEmpty(id);
    }

    /// <summary>
    /// The landmarks of one world, looked up by id. Plain C#; lookups do not
    /// allocate. Missions and navigation read it; the scene builder places the
    /// visible landmarks from the same data, so the two cannot disagree.
    /// </summary>
    public sealed class LandmarkRegistry
    {
        readonly Landmark[] _landmarks;

        public LandmarkRegistry(Landmark[] landmarks)
        {
            _landmarks = landmarks != null ? (Landmark[])landmarks.Clone() : new Landmark[0];
            for (int i = 0; i < _landmarks.Length; i++)
            {
                if (!_landmarks[i].IsDefined) throw new ArgumentException("Every landmark needs an id.", nameof(landmarks));
                for (int j = 0; j < i; j++)
                    if (_landmarks[j].id == _landmarks[i].id) throw new ArgumentException("Duplicate landmark id: " + _landmarks[i].id, nameof(landmarks));
            }
        }

        public int Count => _landmarks.Length;

        public Landmark this[int index] => _landmarks[index];

        /// <summary>Index of a landmark, or -1.</summary>
        public int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < _landmarks.Length; i++)
                if (string.Equals(_landmarks[i].id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        public bool TryGet(string id, out Landmark landmark)
        {
            int index = IndexOf(id);
            landmark = index >= 0 ? _landmarks[index] : default;
            return index >= 0;
        }

        /// <summary>The first landmark of a kind (for example the airfield), if any.</summary>
        public bool TryGetFirst(LandmarkKind kind, out Landmark landmark)
        {
            for (int i = 0; i < _landmarks.Length; i++)
            {
                if (_landmarks[i].kind != kind) continue;
                landmark = _landmarks[i];
                return true;
            }
            landmark = default;
            return false;
        }
    }
}
