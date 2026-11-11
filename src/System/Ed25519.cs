// Ed25519.cs - Ed25519 signatures (RFC 8032), which .NET Framework 4.8 lacks: how the app signs what it sends the floor,
// and checks what the floor sends it. A port of TweetNaCl's (tweetnacl.cr.yp.to, public domain), kept as plain as it was.
//
// 1. Key(seed)                  the public key of a 32-byte private seed
// 2. Sign(seed, message)        a 64-byte signature
// 3. Check(key, message, sig)   whether key signed message
using System.Security.Cryptography;

namespace Onsay.Desktop;

static class Ed25519
{
    ////////// FIELD //////////

    // A number mod 2^255 - 19, as 16 limbs of 16 bits.
    static long[] Gf(params long[] v)
    {
        var o = new long[16];
        Array.Copy(v, o, v.Length);
        return o;
    }

    static readonly long[] Zero = Gf(), One = Gf(1);
    static readonly long[] D = Gf(0x78a3, 0x1359, 0x4dca, 0x75eb, 0xd8ab, 0x4141, 0x0a4d, 0x0070, 0xe898, 0x7779, 0x4079, 0x8cc7, 0xfe73, 0x2b6f, 0x6cee, 0x5203);
    static readonly long[] D2 = Gf(0xf159, 0x26b2, 0x9b94, 0xebd6, 0xb156, 0x8283, 0x149a, 0x00e0, 0xd130, 0xeef3, 0x80f2, 0x198e, 0xfce7, 0x56df, 0xd9dc, 0x2406);
    static readonly long[] X = Gf(0xd51a, 0x8f25, 0x2d60, 0xc956, 0xa7b2, 0x9525, 0xc760, 0x692c, 0xdc5c, 0xfdd6, 0xe231, 0xc0a4, 0x53fe, 0xcd6e, 0x36d3, 0x2169);
    static readonly long[] Y = Gf(0x6658, 0x6666, 0x6666, 0x6666, 0x6666, 0x6666, 0x6666, 0x6666, 0x6666, 0x6666, 0x6666, 0x6666, 0x6666, 0x6666, 0x6666, 0x6666);
    static readonly long[] I = Gf(0xa0b0, 0x4a0e, 0x1b27, 0xc4ee, 0xe478, 0xad2f, 0x1806, 0x2f43, 0xd7a7, 0x3dfb, 0x0099, 0x2b4d, 0xdf0b, 0x4fc1, 0x2480, 0x2b83);
    static readonly long[] L = { 0xed, 0xd3, 0xf5, 0x5c, 0x1a, 0x63, 0x12, 0x58, 0xd6, 0x9c, 0xf7, 0xa2, 0xde, 0xf9, 0xde, 0x14, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x10 };

    static void Set(long[] o, long[] a) => Array.Copy(a, o, 16);

    static void Carry(long[] o)
    {
        for (int i = 0; i < 16; i++)
        {
            o[i] += 1L << 16;
            long c = o[i] >> 16;
            if (i < 15) o[i + 1] += c - 1;
            else o[0] += 38 * (c - 1);
            o[i] -= c << 16;
        }
    }

    // Swaps p and q when b is 1, in the same time either way.
    static void Select(long[] p, long[] q, long b)
    {
        long c = ~(b - 1);
        for (int i = 0; i < 16; i++)
        {
            long t = c & (p[i] ^ q[i]);
            p[i] ^= t;
            q[i] ^= t;
        }
    }

    static byte[] Pack(long[] n)
    {
        var o = new byte[32];
        var t = (long[])n.Clone();
        var m = new long[16];
        Carry(t);
        Carry(t);
        Carry(t);
        for (int j = 0; j < 2; j++)
        {
            m[0] = t[0] - 0xffed;
            for (int i = 1; i < 15; i++)
            {
                m[i] = t[i] - 0xffff - ((m[i - 1] >> 16) & 1);
                m[i - 1] &= 0xffff;
            }
            m[15] = t[15] - 0x7fff - ((m[14] >> 16) & 1);
            long b = (m[15] >> 16) & 1;
            m[14] &= 0xffff;
            Select(t, m, 1 - b);
        }
        for (int i = 0; i < 16; i++)
        {
            o[2 * i] = (byte)(t[i] & 0xff);
            o[2 * i + 1] = (byte)(t[i] >> 8);
        }
        return o;
    }

    static bool Differ(long[] a, long[] b) => !Pack(a).SequenceEqual(Pack(b));

    static int Parity(long[] a) => Pack(a)[0] & 1;

    static void Unpack(long[] o, byte[] n)
    {
        for (int i = 0; i < 16; i++) o[i] = n[2 * i] + ((long)n[2 * i + 1] << 8);
        o[15] &= 0x7fff;
    }

    static void Add(long[] o, long[] a, long[] b)
    {
        for (int i = 0; i < 16; i++) o[i] = a[i] + b[i];
    }

    static void Sub(long[] o, long[] a, long[] b)
    {
        for (int i = 0; i < 16; i++) o[i] = a[i] - b[i];
    }

    static void Mul(long[] o, long[] a, long[] b)
    {
        var t = new long[31];
        for (int i = 0; i < 16; i++)
            for (int j = 0; j < 16; j++) t[i + j] += a[i] * b[j];
        for (int i = 0; i < 15; i++) t[i] += 38 * t[i + 16];
        Array.Copy(t, o, 16);
        Carry(o);
        Carry(o);
    }

    static void Inverse(long[] o, long[] i)
    {
        var c = (long[])i.Clone();
        for (int a = 253; a >= 0; a--)
        {
            Mul(c, c, c);
            if (a != 2 && a != 4) Mul(c, c, i);
        }
        Set(o, c);
    }

    static void Pow2523(long[] o, long[] i)
    {
        var c = (long[])i.Clone();
        for (int a = 250; a >= 0; a--)
        {
            Mul(c, c, c);
            if (a != 1) Mul(c, c, i);
        }
        Set(o, c);
    }

    ////////// CURVE //////////

    // A point as four field numbers, extended coordinates.
    static long[][] Point() => new[] { Gf(), Gf(), Gf(), Gf() };

    static void Plus(long[][] p, long[][] q)
    {
        long[] a = Gf(), b = Gf(), c = Gf(), d = Gf(), t = Gf(), e = Gf(), f = Gf(), g = Gf(), h = Gf();
        Sub(a, p[1], p[0]);
        Sub(t, q[1], q[0]);
        Mul(a, a, t);
        Add(b, p[0], p[1]);
        Add(t, q[0], q[1]);
        Mul(b, b, t);
        Mul(c, p[3], q[3]);
        Mul(c, c, D2);
        Mul(d, p[2], q[2]);
        Add(d, d, d);
        Sub(e, b, a);
        Sub(f, d, c);
        Add(g, d, c);
        Add(h, b, a);
        Mul(p[0], e, f);
        Mul(p[1], h, g);
        Mul(p[2], g, f);
        Mul(p[3], e, h);
    }

    static byte[] PackPoint(long[][] p)
    {
        long[] tx = Gf(), ty = Gf(), zi = Gf();
        Inverse(zi, p[2]);
        Mul(tx, p[0], zi);
        Mul(ty, p[1], zi);
        var r = Pack(ty);
        r[31] ^= (byte)(Parity(tx) << 7);
        return r;
    }

    static void Times(long[][] p, long[][] q, byte[] s)
    {
        Set(p[0], Zero);
        Set(p[1], One);
        Set(p[2], One);
        Set(p[3], Zero);
        for (int i = 255; i >= 0; i--)
        {
            int b = (s[i / 8] >> (i & 7)) & 1;
            for (int k = 0; k < 4; k++) Select(p[k], q[k], b);
            Plus(q, p);
            Plus(p, p);
            for (int k = 0; k < 4; k++) Select(p[k], q[k], b);
        }
    }

    static void TimesBase(long[][] p, byte[] s)
    {
        var q = Point();
        Set(q[0], X);
        Set(q[1], Y);
        Set(q[2], One);
        Mul(q[3], X, Y);
        Times(p, q, s);
    }

    // The negative of the point a key packs, or null when it isn't one.
    static long[][]? UnpackNegative(byte[] p)
    {
        var r = Point();
        long[] t = Gf(), chk = Gf(), num = Gf(), den = Gf(), den2 = Gf(), den4 = Gf(), den6 = Gf();
        Set(r[2], One);
        Unpack(r[1], p);
        Mul(num, r[1], r[1]);
        Mul(den, num, D);
        Sub(num, num, r[2]);
        Add(den, r[2], den);
        Mul(den2, den, den);
        Mul(den4, den2, den2);
        Mul(den6, den4, den2);
        Mul(t, den6, num);
        Mul(t, t, den);
        Pow2523(t, t);
        Mul(t, t, num);
        Mul(t, t, den);
        Mul(t, t, den);
        Mul(r[0], t, den);
        Mul(chk, r[0], r[0]);
        Mul(chk, chk, den);
        if (Differ(chk, num)) Mul(r[0], r[0], I);
        Mul(chk, r[0], r[0]);
        Mul(chk, chk, den);
        if (Differ(chk, num)) return null;
        if (Parity(r[0]) == (p[31] >> 7)) Sub(r[0], Zero, r[0]);
        Mul(r[3], r[0], r[1]);
        return r;
    }

    ////////// SCALARS //////////

    // x mod the group's order, into 32 bytes.
    static byte[] ModL(long[] x)
    {
        var r = new byte[32];
        long carry;
        for (int i = 63; i >= 32; i--)
        {
            carry = 0;
            int j;
            for (j = i - 32; j < i - 12; j++)
            {
                x[j] += carry - 16 * x[i] * L[j - (i - 32)];
                carry = (x[j] + 128) >> 8;
                x[j] -= carry << 8;
            }
            x[j] += carry;
            x[i] = 0;
        }
        carry = 0;
        for (int j = 0; j < 32; j++)
        {
            x[j] += carry - (x[31] >> 4) * L[j];
            carry = x[j] >> 8;
            x[j] &= 255;
        }
        for (int j = 0; j < 32; j++) x[j] -= carry * L[j];
        for (int i = 0; i < 32; i++)
        {
            x[i + 1] += x[i] >> 8;
            r[i] = (byte)(x[i] & 255);
        }
        return r;
    }

    static byte[] Reduce(byte[] h)
    {
        var x = new long[64];
        for (int i = 0; i < 64; i++) x[i] = h[i];
        return ModL(x);
    }

    static byte[] Sha512(params byte[][] parts)
    {
        using var sha = SHA512.Create();
        return sha.ComputeHash(parts.SelectMany(p => p).ToArray());
    }

    // The seed's hash, its first half clamped as the secret scalar.
    static byte[] Expanded(byte[] seed)
    {
        var d = Sha512(seed);
        d[0] &= 248;
        d[31] &= 127;
        d[31] |= 64;
        return d;
    }

    ////////// KEYS AND SIGNATURES //////////

    // 1.
    public static byte[] Key(byte[] seed)
    {
        var p = Point();
        TimesBase(p, Expanded(seed).Take(32).ToArray());
        return PackPoint(p);
    }

    // 2.
    public static byte[] Sign(byte[] seed, byte[] message)
    {
        var d = Expanded(seed);
        var key = Key(seed);
        var r = Reduce(Sha512(d.Skip(32).ToArray(), message));
        var p = Point();
        TimesBase(p, r);
        var big = PackPoint(p);
        var h = Reduce(Sha512(big, key, message));
        var x = new long[64];
        for (int i = 0; i < 32; i++) x[i] = r[i];
        for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++) x[i + j] += h[i] * (long)d[j];
        return big.Concat(ModL(x)).ToArray();
    }

    // 3.
    public static bool Check(byte[] key, byte[] message, byte[] sig)
    {
        if (key.Length != 32 || sig.Length != 64) return false;
        var q = UnpackNegative(key);
        if (q is null) return false;
        var h = Reduce(Sha512(sig.Take(32).ToArray(), key, message));
        var p = Point();
        Times(p, q, h);
        var b = Point();
        TimesBase(b, sig.Skip(32).ToArray());
        Plus(p, b);
        return PackPoint(p).SequenceEqual(sig.Take(32));
    }
}
