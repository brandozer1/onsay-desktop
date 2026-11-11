// Compat.cs - what .NET Framework 4.8, the .NET that runs on Windows 7 and later, lacks of what this app is written with:
// the types the C# compiler needs for records, init, required members and ranges like text[1..], and the few small
// helpers .NET 8 has built in.
//
// 1. Compiler types   IsExternalInit, RequiredMember, CompilerFeatureRequired, SetsRequiredMembers, Index and Range
// 2. Compat           Deconstruct, GetValueOrDefault, MinBy, MaxBy, Contains, StartsWith, EndsWith, Split, Clamp,
//                     ProcessPath and Shared, the helpers

////////// 1. COMPILER TYPES //////////

namespace System.Runtime.CompilerServices
{
    static class IsExternalInit { }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
    sealed class RequiredMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;
        public string FeatureName { get; }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, Inherited = false)]
    sealed class SetsRequiredMembersAttribute : Attribute { }
}

namespace System
{
    // A place in a text or list, from its start or, with ^, from its end.
    readonly struct Index
    {
        readonly int value;   // from the end when negative, its ones' complement

        public Index(int value, bool fromEnd = false) => this.value = fromEnd ? ~value : value;
        public static Index Start => new(0);
        public static Index End => new(0, true);
        public bool IsFromEnd => value < 0;
        public int Value => value < 0 ? ~value : value;
        public int GetOffset(int length) => value < 0 ? length + value + 1 : value;
        public static implicit operator Index(int value) => new(value);
    }

    // A part of a text or list, from one Index to another.
    readonly struct Range
    {
        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public Index Start { get; }
        public Index End { get; }
        public static Range StartAt(Index start) => new(start, Index.End);
        public static Range EndAt(Index end) => new(Index.Start, end);
        public static Range All => new(Index.Start, Index.End);

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            return (start, End.GetOffset(length) - start);
        }
    }
}

////////// 2. COMPAT //////////

namespace Onsay
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    static class Compat
    {
        // foreach (var (key, value) in record).
        public static void Deconstruct<K, V>(this KeyValuePair<K, V> pair, out K key, out V value)
        {
            key = pair.Key;
            value = pair.Value;
        }

        public static V? GetValueOrDefault<K, V>(this Dictionary<K, V> d, K key) where K : notnull =>
            d.TryGetValue(key, out var v) ? v : default;

        public static V GetValueOrDefault<K, V>(this Dictionary<K, V> d, K key, V fallback) where K : notnull =>
            d.TryGetValue(key, out var v) ? v : fallback;

        public static T? MinBy<T, K>(this IEnumerable<T> items, Func<T, K> by) => items.OrderBy(by).FirstOrDefault();

        public static T? MaxBy<T, K>(this IEnumerable<T> items, Func<T, K> by) => items.OrderByDescending(by).FirstOrDefault();

        public static bool Contains(this string s, char c) => s.IndexOf(c) >= 0;

        public static bool Contains(this string s, string part, StringComparison how) => s.IndexOf(part, how) >= 0;

        public static bool StartsWith(this string s, char c) => s.Length > 0 && s[0] == c;

        public static bool EndsWith(this string s, char c) => s.Length > 0 && s[s.Length - 1] == c;

        public static string[] Split(this string s, char sep, StringSplitOptions how) => s.Split(new[] { sep }, how);

        public static string[] Split(this string s, string sep, StringSplitOptions how = StringSplitOptions.None) => s.Split(new[] { sep }, how);

        public static double Clamp(double x, double low, double high) => x < low ? low : x > high ? high : x;

        public static int Clamp(int x, int low, int high) => x < low ? low : x > high ? high : x;

        // The program's own file, as Environment.ProcessPath gives it on .NET 8.
        public static string ProcessPath => Process.GetCurrentProcess().MainModule?.FileName ?? "";

        public static readonly Random Shared = new();

        // Arguments as one command line, each quoted the way Windows splits them again, as ArgumentList does on .NET 8.
        public static string Arguments(IEnumerable<string> args) => string.Join(" ", args.Select(Quoted));

        static string Quoted(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var b = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in a)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }
                b.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                b.Append(c);
                slashes = 0;
            }
            b.Append('\\', slashes * 2);
            b.Append('"');
            return b.ToString();
        }
    }
}
