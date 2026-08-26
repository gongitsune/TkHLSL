#ifndef SVD3X3_INCLUDED
#define SVD3X3_INCLUDED

#define FLOAT33_IDENTITY float3x3(float3(1, 0, 0), float3(0, 1, 0), float3(0, 0, 1))

static const float svd_eps = 1e-8;
static const float svd_offdiag_eps = 1e-12;

float3x3 outer_product(float3 a, float3 b)
{
    return float3x3(a.x * b, a.y * b, a.z * b);
}

float3 svd_normalize(float3 v, float eps)
{
    float n = sqrt(dot(v, v));
    return v / max(n, eps);
}

float3 svd_orthogonal_unit(float3 v, float eps)
{
    float3 a = float3(1.0, 0.0, 0.0);
    if (abs(v.x) > 0.9)
    {
        a = float3(0.0, 1.0, 0.0);
    }
    return svd_normalize(cross(v, a), eps);
}

float3x3 svd_transpose(float3x3 m)
{
    return float3x3(
        m[0].x, m[1].x, m[2].x,
        m[0].y, m[1].y, m[2].y,
        m[0].z, m[1].z, m[2].z
    );
}

float3 svd_mul(float3x3 m, float3 v)
{
    return float3(dot(m[0], v), dot(m[1], v), dot(m[2], v));
}

float3 svd_get_col(float3x3 m, int c)
{
    return float3(m[0][c], m[1][c], m[2][c]);
}

void svd_set_col(inout float3x3 m, int c, float3 v)
{
    m[0][c] = v.x;
    m[1][c] = v.y;
    m[2][c] = v.z;
}

float3x3 svd_mul_mat(float3x3 a, float3x3 b)
{
    float3x3 bt = svd_transpose(b);
    return float3x3(
        dot(a[0], bt[0]), dot(a[0], bt[1]), dot(a[0], bt[2]),
        dot(a[1], bt[0]), dot(a[1], bt[1]), dot(a[1], bt[2]),
        dot(a[2], bt[0]), dot(a[2], bt[1]), dot(a[2], bt[2])
    );
}

void svd_apply_jacobi(inout float3x3 b, inout float3x3 v, int p, int q)
{
    float apq = b[p][q];
    if (abs(apq) <= svd_offdiag_eps)
    {
        return;
    }

    float app = b[p][p];
    float aqq = b[q][q];
    float phi = 0.5 * atan2(2.0 * apq, aqq - app);
    float c = cos(phi);
    float s = sin(phi);

    int k;
    [unroll]
    for (k = 0; k < 3; ++k)
    {
        float bpk = b[p][k];
        float bqk = b[q][k];
        b[p][k] = c * bpk - s * bqk;
        b[q][k] = s * bpk + c * bqk;
    }

    [unroll]
    for (k = 0; k < 3; ++k)
    {
        float bkp = b[k][p];
        float bkq = b[k][q];
        b[k][p] = c * bkp - s * bkq;
        b[k][q] = s * bkp + c * bkq;
    }

    b[p][q] = 0.0;
    b[q][p] = 0.0;

    [unroll]
    for (k = 0; k < 3; ++k)
    {
        float vkp = v[k][p];
        float vkq = v[k][q];
        v[k][p] = c * vkp - s * vkq;
        v[k][q] = s * vkp + c * vkq;
    }
}

void svd_swap(inout float sa, inout float sb, inout float3 va, inout float3 vb)
{
    float ts = sa;
    sa = sb;
    sb = ts;
    float3 tv = va;
    va = vb;
    vb = tv;
}

void svd_3x3(float3x3 a, out float3x3 u, out float3x3 s, out float3x3 vOut)
{
    float eps = svd_eps;
    float3x3 b = svd_mul_mat(svd_transpose(a), a);
    float3x3 v = float3x3(
        1.0, 0.0, 0.0,
        0.0, 1.0, 0.0,
        0.0, 0.0, 1.0
    );

    [unroll]
    for (int sweep = 0; sweep < 8; ++sweep)
    {
        svd_apply_jacobi(b, v, 0, 1);
        svd_apply_jacobi(b, v, 0, 2);
        svd_apply_jacobi(b, v, 1, 2);
    }

    float s0 = sqrt(max(b[0][0], eps));
    float s1 = sqrt(max(b[1][1], eps));
    float s2 = sqrt(max(b[2][2], eps));

    float3 v0 = svd_get_col(v, 0);
    float3 v1 = svd_get_col(v, 1);
    float3 v2 = svd_get_col(v, 2);

    if (s0 < s1)
    {
        svd_swap(s0, s1, v0, v1);
    }
    if (s0 < s2)
    {
        svd_swap(s0, s2, v0, v2);
    }
    if (s1 < s2)
    {
        svd_swap(s1, s2, v1, v2);
    }

    v0 = svd_normalize(v0, eps);
    v1 = v1 - dot(v0, v1) * v0;
    if (dot(v1, v1) < eps)
    {
        v1 = svd_orthogonal_unit(v0, eps);
    }
    else
    {
        v1 = svd_normalize(v1, eps);
    }
    v2 = svd_normalize(cross(v0, v1), eps);

    float3 av0 = svd_mul(a, v0);
    float3 av1 = svd_mul(a, v1);
    float3 av2 = svd_mul(a, v2);

    float3 u0 = svd_normalize(av0, eps);
    if (s0 > eps)
    {
        u0 = svd_normalize(av0 / s0, eps);
    }

    float3 u1;
    if (s1 > eps)
    {
        u1 = av1 / s1;
    }
    else
    {
        u1 = svd_orthogonal_unit(u0, eps);
    }
    u1 = u1 - dot(u0, u1) * u0;
    if (dot(u1, u1) < eps)
    {
        u1 = svd_orthogonal_unit(u0, eps);
    }
    else
    {
        u1 = svd_normalize(u1, eps);
    }

    float3 u2 = svd_normalize(cross(u0, u1), eps);
    if (dot(u2, u2) < eps && s2 > eps)
    {
        u2 = svd_normalize(av2 / s2, eps);
    }

    u = float3x3(
        u0.x, u1.x, u2.x,
        u0.y, u1.y, u2.y,
        u0.z, u1.z, u2.z
    );

    s = float3x3(
        s0, 0.0, 0.0,
        0.0, s1, 0.0,
        0.0, 0.0, s2
    );

    vOut = float3x3(
        v0.x, v1.x, v2.x,
        v0.y, v1.y, v2.y,
        v0.z, v1.z, v2.z
    );
}

#endif