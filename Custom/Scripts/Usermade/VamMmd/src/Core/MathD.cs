using System;

namespace VamMmd.Core
{
    // Double precision vector and quaternion for the Unity-free core.
    // Convention: Hamilton product, (a * b) applies b first. world = parentWorld * local.
    // The core works in MMD's own axes (Y up, the model faces -Z, its left hand is at +X).
    public struct V3
    {
        public double x, y, z;

        public V3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

        public static readonly V3 Zero = new V3(0, 0, 0);

        public static V3 operator +(V3 a, V3 b) { return new V3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static V3 operator -(V3 a, V3 b) { return new V3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static V3 operator -(V3 a) { return new V3(-a.x, -a.y, -a.z); }
        public static V3 operator *(V3 a, double s) { return new V3(a.x * s, a.y * s, a.z * s); }
        public static V3 operator *(double s, V3 a) { return new V3(a.x * s, a.y * s, a.z * s); }

        public static double Dot(V3 a, V3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }

        public static V3 Cross(V3 a, V3 b)
        {
            return new V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }

        public double Length { get { return Math.Sqrt(x * x + y * y + z * z); } }
        public double SqrLength { get { return x * x + y * y + z * z; } }

        // Unit vector, or the fallback when the vector is too short to have a direction.
        public V3 NormalizedOr(V3 fallback)
        {
            double l = Length;
            if (l < 1e-12 || double.IsNaN(l)) return fallback;
            return new V3(x / l, y / l, z / l);
        }

        public static V3 Lerp(V3 a, V3 b, double t)
        {
            return new V3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static double Distance(V3 a, V3 b) { return (a - b).Length; }

        public override string ToString()
        {
            return "(" + x.ToString("0.#####") + ", " + y.ToString("0.#####") + ", " + z.ToString("0.#####") + ")";
        }
    }

    public struct Q
    {
        public double x, y, z, w;

        public Q(double x, double y, double z, double w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public static readonly Q Identity = new Q(0, 0, 0, 1);

        public static Q operator *(Q a, Q b)
        {
            return new Q(
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        }

        public Q Conj { get { return new Q(-x, -y, -z, w); } }

        public static double Dot(Q a, Q b) { return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w; }

        // Unit quaternion; identity when the input has no usable length (zeroed keys exist in real files).
        public Q Normalized
        {
            get
            {
                double l = Math.Sqrt(x * x + y * y + z * z + w * w);
                if (l < 1e-9 || double.IsNaN(l)) return Identity;
                return new Q(x / l, y / l, z / l, w / l);
            }
        }

        public V3 Rotate(V3 v)
        {
            // v' = v + 2w(u x v) + 2 u x (u x v), u = (x, y, z)
            double cx = y * v.z - z * v.y, cy = z * v.x - x * v.z, cz = x * v.y - y * v.x;
            double dx = y * cz - z * cy, dy = z * cx - x * cz, dz = x * cy - y * cx;
            return new V3(v.x + 2 * (w * cx + dx), v.y + 2 * (w * cy + dy), v.z + 2 * (w * cz + dz));
        }

        public static Q AxisAngle(V3 axis, double radians)
        {
            V3 a = axis.NormalizedOr(new V3(0, 0, 0));
            if (a.SqrLength == 0) return Identity;
            double h = radians * 0.5, s = Math.Sin(h);
            return new Q(a.x * s, a.y * s, a.z * s, Math.Cos(h));
        }

        // Rotation angle in radians, 0..pi.
        public double Angle
        {
            get
            {
                double s = Math.Sqrt(x * x + y * y + z * z);
                return 2 * Math.Atan2(s, Math.Abs(w));
            }
        }

        // Shortest-path spherical interpolation. t outside 0..1 extrapolates along the same arc.
        public static Q Slerp(Q a, Q b, double t)
        {
            double d = Dot(a, b);
            if (d < 0) { b = new Q(-b.x, -b.y, -b.z, -b.w); d = -d; }
            if (d > 0.9999995)
            {
                return new Q(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t,
                             a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t).Normalized;
            }
            double th = Math.Acos(d), s = Math.Sin(th);
            double ka = Math.Sin((1 - t) * th) / s, kb = Math.Sin(t * th) / s;
            return new Q(a.x * ka + b.x * kb, a.y * ka + b.y * kb, a.z * ka + b.z * kb, a.w * ka + b.w * kb);
        }

        // Smallest rotation that turns direction a onto direction b (neither needs to be unit length).
        public static Q FromTo(V3 a, V3 b)
        {
            V3 ua = a.NormalizedOr(V3.Zero), ub = b.NormalizedOr(V3.Zero);
            if (ua.SqrLength == 0 || ub.SqrLength == 0) return Identity;
            double d = V3.Dot(ua, ub);
            if (d > 1 - 1e-12) return Identity;
            if (d < -1 + 1e-9)
            {
                // Opposite: any axis perpendicular to a.
                V3 p = Math.Abs(ua.x) < 0.9 ? new V3(1, 0, 0) : new V3(0, 1, 0);
                return AxisAngle(V3.Cross(ua, p), Math.PI);
            }
            V3 c = V3.Cross(ua, ub);
            return new Q(c.x, c.y, c.z, 1 + d).Normalized;
        }

        public override string ToString()
        {
            return "(" + x.ToString("0.#####") + ", " + y.ToString("0.#####") + ", " + z.ToString("0.#####") + ", " + w.ToString("0.#####") + ")";
        }
    }
}
