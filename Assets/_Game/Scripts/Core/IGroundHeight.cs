namespace ChibiFantasy.Core
{
    /// <summary>
    /// Where the ground is under a point of a map.
    /// </summary>
    /// <remarks>
    /// <b>The one seam that lets the world stop being flat.</b> Movement carried a
    /// character's Y through unchanged because nothing authoritative knew the ground. This
    /// answers that from authored data -- a height field baked from the environment scene --
    /// so the server can place a walking character on a hill without loading a scene or
    /// touching physics.
    ///
    /// <b>It also says where one may not walk.</b> Water, the void beyond a map's edge and
    /// a cliff face are all "no ground here": <see cref="TrySample"/> returns false and the
    /// caller refuses the step. That is what keeps click-to-move honest on a sloped map: the
    /// client raycast and the server height field agree on where the floor is, and only the
    /// server's answer moves anyone.
    /// </remarks>
    public interface IGroundHeight
    {
        /// <summary>
        /// Looks up the ground height at a horizontal position.
        /// </summary>
        /// <param name="x">World X.</param>
        /// <param name="z">World Z.</param>
        /// <param name="height">The ground's Y there, when there is walkable ground.</param>
        /// <returns>False when the point is outside the field or not walkable.</returns>
        bool TrySample(float x, float z, out float height);
    }
}
