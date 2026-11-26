namespace Mammoth
{
    /// <summary>
    /// A stretch of track with a gameplay effect, placed along the forward (z)
    /// axis. Looked up by MammothController.Update() against the mammoth's
    /// current travel distance; purely visual elsewhere (no physics triggers).
    /// </summary>
    public enum MammothTerrainType
    {
        Normal,
        /// <summary>Slick ice: the mammoth drifts side to side and is harder to steer.</summary>
        Ice,
        /// <summary>Mud: travel speed is cut while inside the patch.</summary>
        Mud,
    }

    [System.Serializable]
    public struct MammothTerrainSegment
    {
        public float startZ;
        public float endZ;
        public MammothTerrainType type;

        public bool Contains(float z) => z >= startZ && z < endZ;
    }
}
