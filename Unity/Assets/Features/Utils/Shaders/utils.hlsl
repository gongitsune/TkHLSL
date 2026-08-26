// TkHLSL は引数付き（関数形式）#define に未対応なため、3x3x3固定のループマクロにしている
#define UNROLLED_CUBIC_FOR_3X3X3 \
    [unroll] for (int i = 0; i < 3; ++i) \
    [unroll] for (int j = 0; j < 3; ++j) \
    [unroll] for (int k = 0; k < 3; ++k)
#define PI 3.14159265358979323846

float3 pow2(float3 x)
{
    return x * x;
}

// ----------------------
// define
// ----------------------
#define VEL_FP_SCALE 1e4
#define VEL_FP_SCALE_INV 1e-4
#define WEIGHT_FP_SCALE 1e6
#define WEIGHT_FP_SCALE_INV 1e-6

// IbukiHash by Andante (https://twitter.com/andanteyk)
// This work is marked with CC0 1.0. To view a copy of this license, visit https://creativecommons.org/publicdomain/zero/1.0/
float rand(float4 v)
{
    const uint4 mult =
        uint4(0xae3cc725, 0x9fe72885, 0xae36bfb5, 0x82c1fcad);

    uint4 u = uint4(v);
    u = u * mult;
    u ^= u.wxyz ^ u >> 13;

    uint r = dot(u, mult);

    r ^= r >> 11;
    r = (r * r) ^ r;

    return r * 2.3283064365386962890625e-10;
}