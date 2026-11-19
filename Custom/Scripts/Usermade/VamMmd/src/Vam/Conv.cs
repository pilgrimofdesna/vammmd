using UnityEngine;
using VamMmd.Core;

namespace VamMmd
{
    // The core works in MMD axes (model faces -Z, its left hand at +X); a VaM Person faces +Z with
    // its left hand at -X. Both are left-handed, so the change is a half turn about Y, no mirror.
    internal static class Conv
    {
        public static Vector3 ToUnity(V3 v) { return new Vector3(-(float)v.x, (float)v.y, -(float)v.z); }
        public static Quaternion ToUnity(Q q) { return new Quaternion(-(float)q.x, (float)q.y, -(float)q.z, (float)q.w); }
        public static V3 ToMmd(Vector3 v) { return new V3(-v.x, v.y, -v.z); }
        public static Q ToMmd(Quaternion q) { return new Q(-q.x, q.y, -q.z, q.w); }
    }
}
