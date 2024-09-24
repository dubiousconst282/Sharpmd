using System.Numerics;
using System.Text;

using Sharpmd;

int width = 854;
int height = 480;
var framebuffer = new byte[width * height * 3];

var spheres = new Sphere[] {
    new() { Center = new Vector3(0, 0, -1), Radius = 0.5f, MaterialId = 1 },
    new() { Center = new Vector3(0, -100.5f, -1), Radius = 100f, MaterialId = 2 }
};
var projMat = Matrix4x4.CreatePerspectiveFieldOfView(float.DegreesToRadians(80), width / (float)height, 0.001f, float.PositiveInfinity);

Matrix4x4.Invert(projMat, out var invProj);
invProj = Matrix4x4.CreateScale(2.0f / width, 2.0f / height, 1.0f) *
          Matrix4x4.CreateTranslation(-1.0f, -1.0f, 0.0f) *
          Matrix4x4.CreateScale(1.0f, -1.0f, 1.0f) *
          invProj;

SpmdRunner.DispatchRange2D(width, height, (x, y) => {
    Vector3 finalColor = new(0);
    int numSamples = 64;

    for (int sampleNo = 0; sampleNo < numSamples; sampleNo++) {
        Vector2 subpixelJitter = MartinR2(sampleNo);
        Vector4 wfar = Vector4.Transform(new Vector4(x + subpixelJitter.X, y + subpixelJitter.Y, 0, 1), invProj);
        Vector3 dir = Vector3.Normalize(new Vector3(wfar.X, wfar.Y, wfar.Z) / wfar.W);
        Vector3 origin = new(0);
        
        Vector3 radiance = new(0.0f);
        Vector3 throughput = new(1.0f);

        for (int bounceNo = 0; bounceNo < 5; bounceNo++) {
            float tmin = float.PositiveInfinity;
            Vector3 normal = default;
            int materialId = 0;

            foreach (var obj in spheres) {
                if (obj.Intersect(origin, dir, ref tmin, ref normal)) {
                    materialId = obj.MaterialId;
                }
            }
            if (materialId != 0) {
                Vector3 materialColor = new Vector3(0.5f);
                float emissionStrength = 0;

                throughput *= materialColor;
                radiance += throughput * emissionStrength;

                Vector3 hitPos = origin + dir * tmin;
                origin = hitPos + normal * 0.01f;

                Vector2 noiseSample = RandomSample();
                dir = Vector3.Normalize(normal + SampleDirection(noiseSample));  // lambertian
            } else {
                float a = (dir.Y + 1.0f) * 0.5f;
                Vector3 skyColor = Vector3.Lerp(new Vector3(1.0f), new Vector3(0.5f, 0.7f, 1.0f), a);

                radiance += throughput * skyColor;
                break;
            }
        }
        finalColor += Vector3.SquareRoot(radiance); // gamma correct
    }

    finalColor = Vector3.Clamp(finalColor * (255.0f / numSamples), new Vector3(0), new Vector3(255));
    framebuffer[(x + y * width) * 3 + 0] = (byte)finalColor.X;
    framebuffer[(x + y * width) * 3 + 1] = (byte)finalColor.Y;
    framebuffer[(x + y * width) * 3 + 2] = (byte)finalColor.Z;
});

using var fs = File.Create("raytrace.ppm");
fs.Write(Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
fs.Write(framebuffer);

// Returns two random uniform values in [0..1) range.
static Vector2 RandomSample() {
    int value = Random.Shared.Next();
    return new Vector2(value & 0x7FFF, value >>> 16) / 32768.0f;
}
// Returns unit sphere direction from given random sample
static Vector3 SampleDirection(Vector2 sample) {
    float a = sample.X * MathF.Tau;
    float y = sample.Y * 2.0f - 1.0f;

    float r = MathF.Sqrt(1.0f - y * y);  // sin(y)
    return new Vector3(MathF.Sin(a) * r, y, MathF.Cos(a) * r);
}
// R2 quasirandom sequence
static Vector2 MartinR2(int index) {
    // return fract(index * float2(0.75487766624669276005, 0.56984029099805326591) + 0.5);
    float x = index * 0.75487766624669276005f + 0.5f;
    float y = index * 0.56984029099805326591f + 0.5f;
    return new Vector2(x - MathF.Floor(x), y - MathF.Floor(y));
}

struct Sphere {
    public Vector3 Center;
    public float Radius;
    public int MaterialId;

    public readonly bool Intersect(Vector3 origin, Vector3 dir, ref float hitT, ref Vector3 normal) {
        Vector3 oc = origin - Center;
        float b = Vector3.Dot(oc, dir);
        float c = oc.LengthSquared() - Radius * Radius;
        float h = b * b - c;

        if (h < 0.0) return false; // no intersection

        h = MathF.Sqrt(h);

        float t1 = -b - h, t2 = -b + h;
        if (t2 < 0) return false;

        float t = t1 < 0 ? t2 : t1;
        if (t > hitT) return false;

        hitT = t;
        normal = Vector3.Normalize((origin + dir * hitT) - Center);
        return true;
    }
}
/*
struct Box {
    public Vector3 Min, Max;
    public int MaterialId;

    public bool Intersect(Vector3 origin, Vector3 invDir, ref float hitT, ref Vector3 normal) {
        Vector3 t0 = (Min - origin) * invDir;
        Vector3 t1 = (Max - origin) * invDir;

        Vector3 temp = t0;
        t0 = Vector3.Min(temp, t1);
        t1 = Vector3.Max(temp, t1);

        float tmin = MathF.Max(MathF.Max(t0.X, t0.Y), t0.Z);
        float tmax = MathF.Min(MathF.Min(t1.X, t1.Y), t1.Z);

        normal = default;
        if (tmin < tmax && tmax > 0) {
            return tmin;
        }
        return false;
    }
}*/

struct MaterialData {
    
}