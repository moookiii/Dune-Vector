using System;
using UnityEngine;

namespace DuneVector
{
    [Serializable]
    public sealed class DuneFieldSettings
    {
        [Header("World")]
        public int WorldSeed = 19770503;
        public float BaseHeight = 0f;
        [Min(0f)] public float HeightMultiplier = 1f;

        [Header("Rolling elevation")]
        [Tooltip("Adds broad, low-frequency elevation beneath the existing dune shapes.")]
        public bool RollingElevationEnabled;
        [Tooltip("World-space size of broad elevation changes layered beneath the existing dune preset.")]
        [Min(1f)] public float RollingElevationScale = 800f;
        [Tooltip("Maximum influence of the broad rolling elevation layer. Zero preserves the preset's original terrain profile.")]
        [Min(0f)] public float RollingElevationAmplitude = 0f;
        [Tooltip("World-space offset used to reposition the broad elevation pattern without changing the world seed.")]
        public Vector2 RollingElevationOffset = new Vector2(830f, -470f);
        [Tooltip("World-space size of the noise that bends the broad elevation pattern.")]
        [Min(1f)] public float RollingElevationWarpScale = 1400f;
        [Tooltip("Maximum domain-warp displacement as a fraction of the rolling elevation scale.")]
        [Range(0f, 1f)] public float RollingElevationWarpStrength = 0.35f;
        [Range(1, 4)] public int RollingElevationWarpOctaves = 2;
        [Range(1, 6)] public int RollingElevationOctaves = 3;
        [Range(0.1f, 0.9f)] public float RollingElevationPersistence = 0.48f;
        [Range(1.1f, 4f)] public float RollingElevationLacunarity = 2.03f;

        [Header("Large-scale land form")]
        [Min(1f)] public float MajorScale = 280f;
        [Min(0f)] public float MajorAmplitude = 4.2f;
        [Range(1, 8)] public int MajorOctaves = 4;
        [Range(0.1f, 0.9f)] public float MajorPersistence = 0.48f;
        [Range(1.1f, 4f)] public float MajorLacunarity = 2.03f;
        [Range(0f, 1f)] public float BroadBowlStrength = 0.34f;

        [Header("Directional dune field")]
        [Min(1f)] public float DuneScale = 52f;
        [Min(0f)] public float DuneAmplitude = 5.8f;
        public Vector2 WindDirection = new Vector2(0.92f, 0.38f);
        [Range(0f, 2f)] public float DuneWarp = 0.7f;
        [Range(1, 8)] public int WarpOctaves = 3;
        [Range(0f, 1f)] public float PrimaryRidgeWeight = 0.62f;
        [Range(0f, 1f)] public float RidgeHarmonicWeight = 0.18f;
        [Range(1f, 5f)] public float RidgeHarmonicFrequency = 2f;
        [Range(-3.14f, 3.14f)] public float RidgeHarmonicPhase = 0.8f;
        [Range(0f, 0.75f)] public float CrestVariationStrength = 0.22f;

        [Header("Secondary variation")]
        [Min(1f)] public float SecondaryScale = 105f;
        [Min(0f)] public float SecondaryAmplitude = 2.1f;
        [Range(1, 8)] public int SecondaryOctaves = 3;
        [Range(0.1f, 0.9f)] public float SecondaryPersistence = 0.48f;
        [Range(1.1f, 4f)] public float SecondaryLacunarity = 2.03f;

        [Header("Fine detail")]
        [Min(1f)] public float DetailScale = 19f;
        [Min(0f)] public float DetailAmplitude = 0.35f;
        [Range(1, 6)] public int DetailOctaves = 2;
        [Range(0.1f, 0.9f)] public float DetailPersistence = 0.48f;
        [Range(1.1f, 4f)] public float DetailLacunarity = 2.03f;

        [Header("Surface sampling")]
        [Range(0.1f, 3f)] public float NormalSampleDistance = 0.75f;

        public void CopyRollingElevationFrom(DuneFieldSettings source)
        {
            if (source == null)
            {
                return;
            }

            RollingElevationEnabled = source.RollingElevationEnabled;
            RollingElevationScale = source.RollingElevationScale;
            RollingElevationAmplitude = source.RollingElevationAmplitude;
            RollingElevationOffset = source.RollingElevationOffset;
            RollingElevationWarpScale = source.RollingElevationWarpScale;
            RollingElevationWarpStrength = source.RollingElevationWarpStrength;
            RollingElevationWarpOctaves = source.RollingElevationWarpOctaves;
            RollingElevationOctaves = source.RollingElevationOctaves;
            RollingElevationPersistence = source.RollingElevationPersistence;
            RollingElevationLacunarity = source.RollingElevationLacunarity;
        }
    }

    public readonly struct LogicalPosition
    {
        public readonly double X;
        public readonly double Z;

        public LogicalPosition(double x, double z)
        {
            X = x;
            Z = z;
        }

        public override string ToString()
        {
            return $"({X:0.0}, {Z:0.0})";
        }
    }

    public static class DuneVectorMath
    {
        public static float Sharpness(float sharpness, float deltaTime)
        {
            return sharpness <= 0f ? 1f : 1f - Mathf.Exp(-sharpness * deltaTime);
        }

        public static uint Hash(int x, int z, int seed, int salt = 0)
        {
            return (uint)(Hash64(x, z, seed, salt) >> 32);
        }

        public static float Hash01(int x, int z, int seed, int salt = 0)
        {
            return (Hash(x, z, seed, salt) & 0x00ffffffu) / 16777215f;
        }

        public static float HashRange(int x, int z, int seed, int salt, float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, Hash01(x, z, seed, salt));
        }

        public static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (!string.IsNullOrEmpty(value))
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        hash ^= value[i];
                        hash *= 16777619u;
                    }
                }
                return hash;
            }
        }

        public static double ValueNoise(double x, double z, int seed, int salt = 0)
        {
            long x0 = (long)Math.Floor(x);
            long z0 = (long)Math.Floor(z);
            long x1 = x0 + 1;
            long z1 = z0 + 1;

            double tx = Quintic(x - x0);
            double tz = Quintic(z - z0);
            double a = Lerp(HashSigned(x0, z0, seed, salt), HashSigned(x1, z0, seed, salt), tx);
            double b = Lerp(HashSigned(x0, z1, seed, salt), HashSigned(x1, z1, seed, salt), tx);
            return Lerp(a, b, tz);
        }

        public static double FractalNoise(
            double x,
            double z,
            int seed,
            int salt,
            int octaves = 4,
            double persistence = 0.48,
            double lacunarity = 2.03)
        {
            double value = 0.0;
            double amplitude = 0.56;
            double frequency = 1.0;
            double amplitudeSum = 0.0;

            for (int i = 0; i < octaves; i++)
            {
                value += ValueNoise(x * frequency, z * frequency, seed, salt + (i * 37)) * amplitude;
                amplitudeSum += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return amplitudeSum > 0.0 ? value / amplitudeSum : 0.0;
        }

        private static double HashSigned(long x, long z, int seed, int salt)
        {
            ulong hash = Hash64(x, z, seed, salt);
            double unit = (hash >> 11) * (1.0 / 9007199254740991.0);
            return (unit * 2.0) - 1.0;
        }

        private static ulong Hash64(long x, long z, int seed, int salt)
        {
            unchecked
            {
                ulong hash = (ulong)x * 0x9E3779B185EBCA87UL;
                hash ^= (ulong)z * 0xC2B2AE3D27D4EB4FUL;
                hash ^= (uint)seed * 0x165667B19E3779F9UL;
                hash ^= (uint)salt * 0x85EBCA77C2B2AE63UL;
                hash ^= hash >> 30;
                hash *= 0xBF58476D1CE4E5B9UL;
                hash ^= hash >> 27;
                hash *= 0x94D049BB133111EBUL;
                hash ^= hash >> 31;
                return hash;
            }
        }

        private static double Quintic(double value)
        {
            return value * value * value * ((value * ((value * 6.0) - 15.0)) + 10.0);
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + ((b - a) * t);
        }
    }

    public sealed class DuneHeightField
    {
        private readonly DuneFieldSettings _settings;
        private readonly double _windX;
        private readonly double _windZ;
        private readonly double _crossX;
        private readonly double _crossZ;

        public DuneFieldSettings Settings => _settings;

        public DuneHeightField(DuneFieldSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Vector2 wind = settings.WindDirection.sqrMagnitude > 0.0001f
                ? settings.WindDirection.normalized
                : Vector2.right;
            _windX = wind.x;
            _windZ = wind.y;
            _crossX = -wind.y;
            _crossZ = wind.x;
        }

        public double SampleHeight(double worldX, double worldZ)
        {
            int seed = _settings.WorldSeed;

            double rollingElevation = 0.0;
            if (_settings.RollingElevationEnabled && _settings.RollingElevationAmplitude > 0f)
            {
                double rollingScale = Math.Max(1.0, _settings.RollingElevationScale);
                double warpScale = Math.Max(1.0, _settings.RollingElevationWarpScale);
                double warpStrength = Math.Max(0.0, _settings.RollingElevationWarpStrength);
                double warpX = 0.0;
                double warpZ = 0.0;
                if (warpStrength > 0.0)
                {
                    warpX = DuneVectorMath.FractalNoise(
                        worldX / warpScale,
                        worldZ / warpScale,
                        seed,
                        43,
                        _settings.RollingElevationWarpOctaves,
                        _settings.RollingElevationPersistence,
                        _settings.RollingElevationLacunarity);
                    warpZ = DuneVectorMath.FractalNoise(
                        worldX / warpScale,
                        worldZ / warpScale,
                        seed,
                        47,
                        _settings.RollingElevationWarpOctaves,
                        _settings.RollingElevationPersistence,
                        _settings.RollingElevationLacunarity);
                }

                double maximumWarpDistance = rollingScale * warpStrength;
                double rollingX = worldX + _settings.RollingElevationOffset.x + (warpX * maximumWarpDistance);
                double rollingZ = worldZ + _settings.RollingElevationOffset.y + (warpZ * maximumWarpDistance);
                rollingElevation = DuneVectorMath.FractalNoise(
                    rollingX / rollingScale,
                    rollingZ / rollingScale,
                    seed,
                    59,
                    _settings.RollingElevationOctaves,
                    _settings.RollingElevationPersistence,
                    _settings.RollingElevationLacunarity);
            }

            double majorX = worldX / Math.Max(1.0, _settings.MajorScale);
            double majorZ = worldZ / Math.Max(1.0, _settings.MajorScale);
            double major = DuneVectorMath.FractalNoise(
                majorX,
                majorZ,
                seed,
                101,
                _settings.MajorOctaves,
                _settings.MajorPersistence,
                _settings.MajorLacunarity);
            double broadBowl = DuneVectorMath.ValueNoise(majorX * 0.43 + 17.2, majorZ * 0.43 - 9.7, seed, 151);

            double alongWind = (worldX * _windX) + (worldZ * _windZ);
            double acrossWind = (worldX * _crossX) + (worldZ * _crossZ);
            double secondaryScale = Math.Max(1.0, _settings.SecondaryScale);
            double warp = DuneVectorMath.FractalNoise(
                acrossWind / secondaryScale,
                alongWind / (secondaryScale * 1.35),
                seed,
                211,
                _settings.WarpOctaves);

            double phase = (alongWind / Math.Max(1.0, _settings.DuneScale)) * Math.PI * 2.0;
            phase += warp * _settings.DuneWarp * Math.PI;
            double primaryRidge = (Math.Sin(phase) * _settings.PrimaryRidgeWeight)
                + (Math.Sin((phase * _settings.RidgeHarmonicFrequency) + _settings.RidgeHarmonicPhase) * _settings.RidgeHarmonicWeight);
            double crestVariation = DuneVectorMath.ValueNoise(
                acrossWind / (secondaryScale * 0.72),
                alongWind / (secondaryScale * 1.8),
                seed,
                263);
            primaryRidge *= (1.0 - _settings.CrestVariationStrength)
                + (crestVariation * _settings.CrestVariationStrength);

            double secondary = DuneVectorMath.FractalNoise(
                (worldX + 320.0) / secondaryScale,
                (worldZ - 170.0) / secondaryScale,
                seed,
                307,
                _settings.SecondaryOctaves,
                _settings.SecondaryPersistence,
                _settings.SecondaryLacunarity);

            double detailScale = Math.Max(1.0, _settings.DetailScale);
            double detail = DuneVectorMath.FractalNoise(
                worldX / detailScale,
                worldZ / detailScale,
                seed,
                401,
                _settings.DetailOctaves,
                _settings.DetailPersistence,
                _settings.DetailLacunarity);

            double generatedHeight = (rollingElevation * _settings.RollingElevationAmplitude)
                + (major * _settings.MajorAmplitude)
                + (broadBowl * _settings.MajorAmplitude * _settings.BroadBowlStrength)
                + (primaryRidge * _settings.DuneAmplitude)
                + (secondary * _settings.SecondaryAmplitude)
                + (detail * _settings.DetailAmplitude);
            return _settings.BaseHeight + (generatedHeight * _settings.HeightMultiplier);
        }

        public Vector3 SampleNormal(double worldX, double worldZ, double distance = -1.0)
        {
            if (distance <= 0.0)
            {
                distance = Math.Max(0.1, _settings.NormalSampleDistance);
            }
            double left = SampleHeight(worldX - distance, worldZ);
            double right = SampleHeight(worldX + distance, worldZ);
            double back = SampleHeight(worldX, worldZ - distance);
            double forward = SampleHeight(worldX, worldZ + distance);
            Vector3 tangentX = new Vector3((float)(distance * 2.0), (float)(right - left), 0f);
            Vector3 tangentZ = new Vector3(0f, (float)(forward - back), (float)(distance * 2.0));
            return Vector3.Cross(tangentZ, tangentX).normalized;
        }
    }
}
